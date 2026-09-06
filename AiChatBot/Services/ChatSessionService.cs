using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Ai.Service
{
    /// <summary>
    /// Manages chat sessions in SQL Server.
    /// </summary>
    public class ChatSessionService
    {
        private readonly string _conn;

        public ChatSessionService(string conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// Ensures ChatSessions table exists and ChatMessages has a SessionId column.
        /// </summary>
        public async Task EnsureTablesExistAsync()
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string createSessions = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatSessions')
                BEGIN
                    CREATE TABLE ChatSessions (
                        Id        INT PRIMARY KEY IDENTITY(1,1),
                        Title     NVARCHAR(100) NOT NULL DEFAULT 'New Chat',
                        CreatedAt DATETIME      NOT NULL DEFAULT GETDATE()
                    );
                END
            ";

            string addSessionId = @"
                IF NOT EXISTS (
                    SELECT * FROM sys.columns
                    WHERE object_id = OBJECT_ID('ChatMessages') AND name = 'SessionId'
                )
                BEGIN
                    ALTER TABLE ChatMessages
                        ADD SessionId INT NULL
                            REFERENCES ChatSessions(Id) ON DELETE CASCADE;
                END
            ";

            using (var cmd = new SqlCommand(createSessions, connect))
                await cmd.ExecuteNonQueryAsync();

            using (var cmd = new SqlCommand(addSessionId, connect))
                await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Returns all sessions ordered by most recent first.
        /// </summary>
        public async Task<List<(int Id, string Title, DateTime CreatedAt)>> GetAllSessionsAsync()
        {
            var sessions = new List<(int Id, string Title, DateTime CreatedAt)>();

            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string sql = @"
                SELECT Id, Title, CreatedAt
                FROM ChatSessions
                ORDER BY CreatedAt DESC
            ";

            using var cmd = new SqlCommand(sql, connect);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sessions.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetDateTime(2)
                ));
            }

            return sessions;
        }

        /// <summary>
        /// Creates a new session with a default title and returns its ID.
        /// </summary>
        public async Task<int> CreateSessionAsync()
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string sql = @"
                INSERT INTO ChatSessions (Title)
                VALUES ('New Chat');
                SELECT SCOPE_IDENTITY();
            ";

            using var cmd = new SqlCommand(sql, connect);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Updates the session title with the AI-generated title.
        /// </summary>
        public async Task UpdateSessionTitleAsync(int sessionId, string title)
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string sql = @"
                UPDATE ChatSessions
                SET Title = @Title
                WHERE Id = @Id
            ";

            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@Title", title);
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Deletes a session and all its messages (via CASCADE).
        /// </summary>
        public async Task DeleteSessionAsync(int sessionId)
        {
            using var connect = new SqlConnection(_conn);
            await connect.OpenAsync();

            string sql = "DELETE FROM ChatSessions WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, connect);
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Finds any sessions titled 'New Chat' that have user messages, and generates proper titles for them.
        /// </summary>
        public async Task RepairUntitledSessionsAsync(IChatCompletionService chatService)
        {
            try
            {
                using var connect = new SqlConnection(_conn);
                await connect.OpenAsync();

                string sql = @"
                    SELECT s.Id,
                           (SELECT TOP 1 Message FROM ChatMessages m WHERE m.SessionId = s.Id AND m.Role = 'user' ORDER BY m.Id ASC) AS FirstMsg
                    FROM ChatSessions s
                    WHERE s.Title = 'New Chat' OR s.Title IS NULL
                ";

                var candidates = new List<(int Id, string FirstMsg)>();

                using (var cmd = new SqlCommand(sql, connect))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(1))
                        {
                            int id = reader.GetInt32(0);
                            string msg = reader.GetString(1);
                            if (!string.IsNullOrWhiteSpace(msg))
                                candidates.Add((id, msg));
                        }
                    }
                }

                foreach (var item in candidates)
                {
                    string title = await TitleGenerator.GenerateTitleAsync(chatService, item.FirstMsg);
                    if (!string.IsNullOrWhiteSpace(title) && !title.Equals("New Chat", StringComparison.OrdinalIgnoreCase))
                    {
                        await UpdateSessionTitleAsync(item.Id, title);
                    }
                }
            }
            catch
            {
                // Non-critical background repair
            }
        }
    }
}
