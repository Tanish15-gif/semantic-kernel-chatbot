using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Text;
using Ai.Models;

namespace Ai.Plugins
{
    /// <summary>
    /// Provides SQL Server database management, schema inspection, and querying operations.
    /// </summary>
    public class DatabasePlugin
    {
        private readonly string _masterConnection;

        public DatabasePlugin(string masterConnection)
        {
            _masterConnection = masterConnection;
        }

        [KernelFunction]
        [Description("Create a new SQL Server database.")]
        public async Task<string> CreateDatabase(string databaseName)
        {
            if (!IsSafeName(databaseName))
                return $"Invalid database name: {databaseName}";

            using var connect = new SqlConnection(_masterConnection);
            await connect.OpenAsync();

            string checkSql = "SELECT DB_ID(@DatabaseName)";
            using var checkCmd = new SqlCommand(checkSql, connect);
            checkCmd.Parameters.AddWithValue("@DatabaseName", databaseName);

            var dbId = await checkCmd.ExecuteScalarAsync();
            if (dbId != DBNull.Value && dbId != null)
                return $"Database '{databaseName}' already exists.";

            string createSql = @"
DECLARE @sql NVARCHAR(MAX);
SET @sql = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
EXEC(@sql);
";
            using var createCmd = new SqlCommand(createSql, connect);
            createCmd.Parameters.AddWithValue("@DatabaseName", databaseName);
            await createCmd.ExecuteNonQueryAsync();

            return $"Database '{databaseName}' created successfully.";
        }

        [KernelFunction]
        [Description("Create a table with custom columns in a SQL Server database.")]
        public async Task<string> CreateTable(string databaseName, string tableName, List<ColumnDefinition> columns)
        {
            if (!IsSafeName(databaseName))
                return $"Invalid database name: {databaseName}";

            if (!IsSafeName(tableName))
                return $"Invalid table name: {tableName}";

            if (columns == null || columns.Count == 0)
                return "At least one column is required.";

            var columnSqlList = new List<string>();

            foreach (var col in columns)
            {
                if (!IsSafeName(col.Name!))
                    return $"Invalid column name: {col.Name}";

                if (!IsSafeType(col.Type!))
                    return $"Invalid SQL type: {col.Type}";

                string columnSql = $"[{col.Name}] {NormalizeSqlType(col.Type!)}";

                if (col.IsIdentity)
                    columnSql += " IDENTITY(1,1)";

                columnSql += col.IsNullable ? " NULL" : " NOT NULL";

                if (col.IsPrimaryKey)
                    columnSql += " PRIMARY KEY";

                columnSqlList.Add(columnSql);
            }

            string columnsSql = string.Join(",\n", columnSqlList);

            var builder = new SqlConnectionStringBuilder(_masterConnection)
            {
                InitialCatalog = databaseName
            };

            string sql = $@"
IF OBJECT_ID(@TableName, 'U') IS NULL
BEGIN
    CREATE TABLE [{tableName}]
    (
        {columnsSql}
    )
END";

            using var connect = new SqlConnection(builder.ConnectionString);
            await connect.OpenAsync();

            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            await cmd.ExecuteNonQueryAsync();

            return $"Table '{tableName}' created successfully in database '{databaseName}' with {columns.Count} columns.";
        }

        [KernelFunction]
        [Description("List all user databases currently on the SQL Server.")]
        public async Task<string> ListDatabases()
        {
            using var connect = new SqlConnection(_masterConnection);
            await connect.OpenAsync();

            string sql = @"
                SELECT name 
                FROM sys.databases 
                WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
                ORDER BY name;
            ";

            using var cmd = new SqlCommand(sql, connect);
            using var reader = await cmd.ExecuteReaderAsync();

            var dbs = new List<string>();
            while (await reader.ReadAsync())
            {
                dbs.Add(reader.GetString(0));
            }

            if (dbs.Count == 0)
                return "No user databases found on the SQL Server.";

            return "Databases:\n" + string.Join("\n", dbs.Select(d => $"  • {d}"));
        }

        [KernelFunction]
        [Description("List all tables in a specific database.")]
        public async Task<string> ListTables(string databaseName)
        {
            if (!IsSafeName(databaseName))
                return $"Invalid database name: {databaseName}";

            var builder = new SqlConnectionStringBuilder(_masterConnection)
            {
                InitialCatalog = databaseName
            };

            using var connect = new SqlConnection(builder.ConnectionString);
            await connect.OpenAsync();

            string sql = @"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME;
            ";

            using var cmd = new SqlCommand(sql, connect);
            using var reader = await cmd.ExecuteReaderAsync();

            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            if (tables.Count == 0)
                return $"No tables found in database '{databaseName}'.";

            return $"Tables in '{databaseName}':\n" + string.Join("\n", tables.Select(t => $"  • {t}"));
        }

        [KernelFunction]
        [Description("Get the schema (columns, data types, nullability, keys) of a specific table.")]
        public async Task<string> DescribeTable(string databaseName, string tableName)
        {
            if (!IsSafeName(databaseName) || !IsSafeName(tableName))
                return "Invalid database or table name.";

            var builder = new SqlConnectionStringBuilder(_masterConnection)
            {
                InitialCatalog = databaseName
            };

            using var connect = new SqlConnection(builder.ConnectionString);
            await connect.OpenAsync();

            string sql = @"
                SELECT 
                    c.COLUMN_NAME, 
                    c.DATA_TYPE, 
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.IS_NULLABLE,
                    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION;
            ";

            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            using var reader = await cmd.ExecuteReaderAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"Columns for '{databaseName}.dbo.{tableName}':");
            sb.AppendLine("  Column Name          | Data Type       | Nullable | Identity");
            sb.AppendLine("  ---------------------|-----------------|----------|---------");

            bool hasRows = false;
            while (await reader.ReadAsync())
            {
                hasRows = true;
                string colName = reader.GetString(0);
                string dataType = reader.GetString(1);
                var maxLen = reader.IsDBNull(2) ? "" : $"({(reader.GetInt32(2) == -1 ? "MAX" : reader.GetInt32(2).ToString())})";
                string fullType = dataType + maxLen;
                string isNullable = reader.GetString(3);
                bool isIdentity = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;

                sb.AppendLine($"  {colName,-20} | {fullType,-15} | {isNullable,-8} | {(isIdentity ? "YES" : "NO"),-7}");
            }

            return hasRows ? sb.ToString() : $"Table '{tableName}' not found in database '{databaseName}'.";
        }

        [KernelFunction]
        [Description("Execute a read-only SELECT query against a database and view results.")]
        public async Task<string> ExecuteSelectQuery(string databaseName, string selectQuery)
        {
            if (!IsSafeName(databaseName))
                return $"Invalid database name: {databaseName}";

            // Enforce read-only constraint
            string trimmed = selectQuery.Trim().TrimStart(';', ' ', '\t', '\r', '\n');
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return "Safety error: Only read-only SELECT queries are permitted via this function.";

            string[] forbidden = { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "EXEC", "EXECUTE" };
            foreach (var word in forbidden)
            {
                if (Regex.IsMatch(trimmed, $@"\b{word}\b", RegexOptions.IgnoreCase))
                    return $"Safety error: '{word}' statements are not allowed in read-only queries.";
            }

            var builder = new SqlConnectionStringBuilder(_masterConnection)
            {
                InitialCatalog = databaseName
            };

            using var connect = new SqlConnection(builder.ConnectionString);
            await connect.OpenAsync();

            using var cmd = new SqlCommand(trimmed, connect);
            cmd.CommandTimeout = 15;
            using var reader = await cmd.ExecuteReaderAsync();

            var sb = new StringBuilder();
            int fieldCount = reader.FieldCount;

            // Headers
            var colNames = new List<string>();
            for (int i = 0; i < fieldCount; i++)
                colNames.Add(reader.GetName(i));

            sb.AppendLine(string.Join(" | ", colNames));
            sb.AppendLine(new string('-', Math.Min(80, sb.Length + 10)));

            int rowCount = 0;
            while (await reader.ReadAsync() && rowCount < 50)
            {
                rowCount++;
                var rowVals = new List<string>();
                for (int i = 0; i < fieldCount; i++)
                {
                    rowVals.Add(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "");
                }
                sb.AppendLine(string.Join(" | ", rowVals));
            }

            sb.AppendLine($"\n({rowCount} row(s) returned)");
            return sb.ToString();
        }

        private static bool IsSafeType(string type)
        {
            string upper = NormalizeSqlType(type);

            string[] allowed =
            {
                "INT",
                "BIGINT",
                "BIT",
                "FLOAT",
                "DATE",
                "DATETIME",
                "DECIMAL(18,2)",
                "NVARCHAR(50)",
                "NVARCHAR(100)",
                "NVARCHAR(255)",
                "NVARCHAR(MAX)",
                "VARCHAR(50)",
                "VARCHAR(100)",
                "VARCHAR(255)"
            };

            return allowed.Contains(upper);
        }

        private static bool IsSafeName(string name)
        {
            return Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
        }

        private static string NormalizeSqlType(string type)
        {
            return type.Trim().ToUpper().Replace(" ", "");
        }
    }
}
