using Microsoft.Data.SqlClient;

namespace Ai.Service
{
    public class ChatMessageService
    {
        private readonly string? _conn;

        public ChatMessageService(string conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// Ensures the base ChatMessages table exists (without SessionId — handled by ChatSessionService).
        /// </summary>
        public async Task EnsureTableExistsAsync()
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();
            string sql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessages')
                BEGIN
                    CREATE TABLE ChatMessages(
                        Id        INT PRIMARY KEY IDENTITY(1,1),
                        Role      VARCHAR(20)   NOT NULL,
                        Message   NVARCHAR(MAX) NOT NULL,
                        CreatedAt DATETIME      DEFAULT GETDATE()
                    );
                END
            ";
            using var cmd = new SqlCommand(sql, connect);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Saves a message linked to a specific session.
        /// </summary>
        public async Task SaveMessageAsync(string role, string message, int sessionId)
        {
            try
            {
                using var connect = new SqlConnection(_conn);
                await connect.OpenAsync();

                string sql = @"
                    INSERT INTO ChatMessages (Role, Message, SessionId)
                    VALUES (@role, @message, @sessionId);
                ";

                using var cmd = new SqlCommand(sql, connect);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.Parameters.AddWithValue("@message", message);
                cmd.Parameters.AddWithValue("@sessionId", sessionId);

                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Non-fatal: conversation continues even if persistence encounters a transient error
            }
        }

        /// <summary>
        /// Loads the most recent N messages for a specific session, in chronological order.
        /// </summary>
        public async Task<List<(string role, string Message)>> LoadRecentMessageAsync(int sessionId, int count = 10)
        {
            var messages = new List<(string Role, string Message)>();

            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string sql = @"
                SELECT Role, Message
                FROM (
                    SELECT TOP(@Count) Id, Role, Message
                    FROM ChatMessages
                    WHERE SessionId = @SessionId
                    ORDER BY Id DESC
                ) x
                ORDER BY Id ASC
            ";

            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@Count", count);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                messages.Add((
                    reader.GetString(0),
                    reader.GetString(1)
                ));
            }

            return messages;
        }
    }
}
