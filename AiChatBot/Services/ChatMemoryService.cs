using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Ai.Service
{
    public class ChatMessageService
    {
        private readonly string? _conn;
        public ChatMessageService(string conn)
        {
            _conn = conn;
        }
        public async Task EnsureTableExistsAsync()
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();
            string sql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessages')
                BEGIN
                    CREATE TABLE ChatMessages(
                        Id INT PRIMARY KEY IDENTITY(1,1),
                        Role VARCHAR(20) NOT NULL,
                        Message NVARCHAR(MAX) NOT NULL,
                        CreatedAt DATETIME DEFAULT GETDATE()
                    );
                END
            ";
            using var cmd = new SqlCommand(sql, connect);
            await cmd.ExecuteNonQueryAsync();
        }
        public async Task SaveMessageAsync(string role, string message)
        {
            using (var connect = new SqlConnection(_conn))
            {
                await connect.OpenAsync();
                string sql = @"
                    Insert into ChatMessages(Role,Message) Values(@role,@message);
                ";
                using (var cmd = new SqlCommand(sql, connect))
                {
                    cmd.Parameters.AddWithValue("@role", role);
                    cmd.Parameters.AddWithValue("@message", message);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        public async Task<List<(string role, string Message)>> LoadRecentMessageAsync(int count = 20)
        {
            var message = new List<(string Role, string Message)>();

            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();
            string sql = @"
                Select Role,Message
                From
                (
                    Select Top(@Count) Id,Role,Message
                    From ChatMessages
                    Order by Id Desc
                ) x
                Order by Id ASC
            ";
            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@Count", count);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                message.Add((
                    reader.GetString(0),
                    reader.GetString(1)
                ));
            }
            return message;
        }
    }
}