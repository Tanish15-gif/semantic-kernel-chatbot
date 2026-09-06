using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Text;
using Ai.Models;

namespace Ai.Plugins
{
    public class DatabasePlugin
    {
        private readonly string _masterConnection;
        public DatabasePlugin(string masterConnection)
        {
            _masterConnection = masterConnection;
        }

        [KernelFunction]
        [Description("Create a SQL Server database")]
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
            {
                return $"Database '{databaseName}' already exists.";
            }

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
        [Description("Create a table with custom columns in a SQL Server database")]
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