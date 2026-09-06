using System.Net.Http.Headers;
using Ai.Loader;
using Ai.Typer;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Ai.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ai.Service
{
    public class ToolRouter
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;
        private readonly ChatMessageService _memory;

        public ToolRouter(
            Kernel kernel,
            IChatCompletionService chatservice,
            ChatMessageService memory)
        {
            _kernel = kernel;
            _chatService = chatservice;
            _memory = memory;
        }

        public async Task<bool> TryHandleAsync(string prompt, ChatHistory chatHistory, string lastuserTopic, int sessionId)
        {
            if (NeedsDatabaseOperation(prompt))
            {
                await HandleDatabaseAsync(prompt, chatHistory, sessionId);
                return true;
            }

            if (NeedsDateTime(prompt))
            {
                await HandleDateTimeAsync(prompt, chatHistory, sessionId);
                return true;
            }

            if (NeedsMath(prompt))
            {
                await HandleMathAsync(prompt, chatHistory, sessionId);
                return true;
            }

            if (NeedsSearch(prompt))
            {
                await HandleSearchAsync(prompt, chatHistory, lastuserTopic, sessionId);
                return true;
            }

            return false;
        }

        #region Intent Detection

        public bool NeedsSearch(string prompt)
        {
            string p = prompt.Trim().ToLower();

            // Filter out programming commands like "new table", "new variable", "new method", "new chat"
            if (Regex.IsMatch(p, @"\bnew\s+(table|database|class|method|function|variable|file|chat|project|feature)\b"))
                return false;

            // Explicit search requests
            if (Regex.IsMatch(p, @"\b(search|look\s+up|google|bing|browse\s+(for|the\s+web)|find\s+online)\b"))
                return true;

            // Current events, news, headlines, trending
            if (Regex.IsMatch(p, @"\b(current\s+events|breaking\s+news|latest\s+news|today'?s\s+news|news\s+(about|on|in)|trending|headlines)\b"))
                return true;

            // Live facts: weather, prices, sports
            if (Regex.IsMatch(p, @"\b(current\s+weather|weather\s+in|stock\s+price|current\s+price|price\s+of|who\s+won|score\s+of)\b"))
                return true;

            // Releases, versions, and new features
            if (Regex.IsMatch(p, @"\b(release\s+date|when\s+(was|is)\s+.*\s+released|features\s+of|features\s+in|what'?s\s+new\s+in)\b"))
                return true;

            // "what is going on", "what happened"
            if (Regex.IsMatch(p, @"\b(what\s+is\s+(the\s+latest|going\s+on)|what\s+happened|who\s+is\s+currently)\b"))
                return true;

            return false;
        }

        public bool NeedsDateTime(string prompt)
        {
            string p = prompt.Trim().ToLower();

            // Reject programming questions about Date / DateTime
            if (p.Contains("c#") || p.Contains("sql") || p.Contains("python") || p.Contains("javascript") || p.Contains("code"))
            {
                if (!p.Contains("what time") && !p.Contains("what date") && !p.Contains("current time") && !p.Contains("current date"))
                    return false;
            }

            // Reject words containing "date" like "update", "candidate", "validate"
            if (Regex.IsMatch(p, @"\b(update|candidate|validate|mandate|dateofbirth)\b"))
                return false;

            // Specific date/time inquiries
            return Regex.IsMatch(p, @"\b(current\s+time|what\s+time\s+is\s+it|tell\s+me\s+the\s+time|what\s+is\s+the\s+time|the\s+time\s+now)\b") ||
                   Regex.IsMatch(p, @"\b(current\s+date|what\s+is\s+(today'?s|the)\s+date|today'?s\s+date|what\s+date\s+is\s+it)\b") ||
                   Regex.IsMatch(p, @"\b(what\s+day\s+is\s+(it|today)|day\s+of\s+the\s+week)\b") ||
                   Regex.IsMatch(p, @"\b(days\s+between|how\s+many\s+days\s+(until|between))\b") ||
                   Regex.IsMatch(p, @"\b(date\s+in\s+\d+\s+days|what\s+date\s+will\s+it\s+be)\b");
        }

        public bool NeedsMath(string prompt)
        {
            string p = prompt.Trim().ToLower();

            if (p.Contains("c++") || p.Contains("c#") || p.Contains("f#") || p.Contains("css"))
                return false;

            // Arithmetic expressions: 2 + 2, 10.5 * 3, (5 + 2) / 3, 2^8, 10 % 3
            if (Regex.IsMatch(p, @"\d+(\.\d+)?\s*[\+\-\*\/\^\%]\s*\d+"))
                return true;

            // Specific mathematical functions
            if (Regex.IsMatch(p, @"\b(sqrt|square\s+root|power|percent|percentage|modulus|calculate|eval|evaluate)\b.*\d+"))
                return true;

            return false;
        }

        public bool NeedsDatabaseOperation(string prompt)
        {
            string p = prompt.Trim().ToLower();

            // Skip instructional/educational prompts
            if (p.StartsWith("how to") || p.StartsWith("how can") || p.StartsWith("what is") ||
                p.StartsWith("explain") || p.StartsWith("teach") || p.Contains("how do i"))
            {
                return false;
            }

            return Regex.IsMatch(p, @"\b(create\s+database|make\s+database|create\s+a\s+database)\b") ||
                   Regex.IsMatch(p, @"\b(create\s+table|make\s+table|create\s+a\s+table)\b") ||
                   Regex.IsMatch(p, @"\b(list\s+databases|show\s+databases|view\s+databases|what\s+databases)\b") ||
                   Regex.IsMatch(p, @"\b(list\s+tables|show\s+tables|view\s+tables|what\s+tables)\b") ||
                   Regex.IsMatch(p, @"\b(describe\s+table|schema\s+of\s+table|columns\s+in\s+table)\b") ||
                   Regex.IsMatch(p, @"\b(select\s+\*\s+from|query\s+table)\b");
        }

        #endregion

        #region Handlers

        private async Task HandleMathAsync(string prompt, ChatHistory chatHistory, int sessionId)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Calculating", ConsoleColor.Cyan);

            string answer = "";

            try
            {
                string p = prompt.ToLower();
                object? toolResult = null;
                string label = "";

                // Check for square root
                var sqrtMatch = Regex.Match(p, @"(sqrt|square\s+root\s+of)\s*(\d+(\.\d+)?)");
                if (sqrtMatch.Success && double.TryParse(sqrtMatch.Groups[2].Value, out double sqrtNum))
                {
                    toolResult = await _kernel.InvokeAsync("MathPlugins", "SquareRoot", new() { ["number"] = sqrtNum });
                    label = $"√{sqrtNum}";
                }
                // Check for percentage (e.g. 20% of 150)
                else if (Regex.IsMatch(p, @"(\d+(\.\d+)?)\s*%\s*(of\s*)?(\d+(\.\d+)?)"))
                {
                    var pctMatch = Regex.Match(p, @"(\d+(\.\d+)?)\s*%\s*(of\s*)?(\d+(\.\d+)?)");
                    double pct = double.Parse(pctMatch.Groups[1].Value);
                    double total = double.Parse(pctMatch.Groups[4].Value);
                    toolResult = await _kernel.InvokeAsync("MathPlugins", "Percentage", new() { ["percentage"] = pct, ["total"] = total });
                    label = $"{pct}% of {total}";
                }
                // Check for expression (e.g. 2 + 2, (15 * 4) + 10, 2^8)
                else
                {
                    var exprMatch = Regex.Match(prompt, @"[\d\.\s\+\-\*\/\^\%\(\)]+");
                    string expr = exprMatch.Success ? exprMatch.Value.Trim() : prompt;

                    toolResult = await _kernel.InvokeAsync("MathPlugins", "EvaluateExpression", new() { ["expression"] = expr });
                    label = expr;
                }

                cts.Cancel();
                await spinnerTask;

                string valStr = toolResult?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(label))
                    answer = $"Result: {valStr}";
                else
                    answer = $"{label} = {valStr}";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n{answer}\n");
                Console.ResetColor();

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer, sessionId);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nMath calculation failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }

        private async Task HandleDateTimeAsync(string prompt, ChatHistory chatHistory, int sessionId)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Checking Time", ConsoleColor.Blue);

            string answer = "";

            try
            {
                string p = prompt.ToLower();

                if (p.Contains("day of the week") || p.Contains("what day"))
                {
                    var day = (await _kernel.InvokeAsync("DatePlugin", "DayOfWeek")).ToString();
                    answer = $"Today is {day}.";
                }
                else if (p.Contains("what time") || p.Contains("current time") || p.Contains("the time"))
                {
                    var time = (await _kernel.InvokeAsync("DatePlugin", "CurrentTime")).ToString();
                    answer = $"The current time is {time}.";
                }
                else if (p.Contains("what is today's date") || p.Contains("current date") || p.Contains("what date") || p.Contains("today's date"))
                {
                    var date = (await _kernel.InvokeAsync("DatePlugin", "CurrentDate")).ToString();
                    answer = $"Today's date is {date}.";
                }
                else if (Regex.IsMatch(p, @"\bdate\s+in\s+(\d+)\s+days\b"))
                {
                    var match = Regex.Match(p, @"\bdate\s+in\s+(\d+)\s+days\b");
                    int days = int.Parse(match.Groups[1].Value);
                    answer = (await _kernel.InvokeAsync("DatePlugin", "AddDays", new() { ["days"] = days })).ToString();
                }
                else if (Regex.IsMatch(p, @"\bdays\s+between\b"))
                {
                    var dates = Regex.Matches(p, @"\d{4}-\d{2}-\d{2}");
                    if (dates.Count == 2)
                    {
                        answer = (await _kernel.InvokeAsync("DatePlugin", "DaysBetween",
                            new() { ["startDate"] = dates[0].Value, ["endDate"] = dates[1].Value })).ToString();
                    }
                    else
                    {
                        var dt = (await _kernel.InvokeAsync("DatePlugin", "CurrentDateTime")).ToString();
                        answer = $"Current date and time: {dt}.";
                    }
                }
                else
                {
                    var dt = (await _kernel.InvokeAsync("DatePlugin", "CurrentDateTime")).ToString();
                    answer = $"Current date and time: {dt}.";
                }

                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n{answer}\n");
                Console.ResetColor();

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer, sessionId);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nDate check failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }

        private async Task HandleSearchAsync(
            string prompt,
            ChatHistory chatHistory,
            string lastUserTopic,
            int sessionId)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Searching Web", ConsoleColor.Yellow);

            string answer = "";
            var renderer = new MarkdownStreamRenderer();

            try
            {
                string searchQuery = BuildSearchQuery(prompt, lastUserTopic);

                var searchResults = (await _kernel.InvokeAsync(
                    "WebSearchPlugin",
                    "Search",
                    new() { ["query"] = searchQuery }
                )).ToString();

                cts.Cancel();
                await spinnerTask;

                var researchHistory = new ChatHistory();
                researchHistory.AddSystemMessage(@"
You are a concise search assistant.
Use the provided search results to answer the user's question directly in 2 to 4 sentences.
Include source URLs if available.");

                researchHistory.AddUserMessage($"Question: {prompt}\nSearch Results:\n{searchResults}");

                Console.ForegroundColor = ConsoleColor.Green;

                // MaxTokens = 500 gives reasoning models enough budget to complete thinking and generate answer
                await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                    researchHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.3,
                        MaxTokens = 500
                    }))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        foreach (char c in chunk.Content)
                        {
                            renderer.WriteChunk(c.ToString());
                            answer += c;
                            await Task.Delay(0);
                        }
                    }
                }

                renderer.Complete();
                Console.ResetColor();

                // If the model produced no content (e.g. reasoning exhausted), fall back to raw search results
                if (string.IsNullOrWhiteSpace(answer))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n{searchResults}\n");
                    Console.ResetColor();
                    answer = searchResults;
                }
                else
                {
                    Console.WriteLine("\n");
                }

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer, sessionId);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nSearch failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }

        private async Task HandleDatabaseAsync(string prompt, ChatHistory chatHistory, int sessionId)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Database", ConsoleColor.Blue);

            try
            {
                var planHistory = new ChatHistory();
                planHistory.AddSystemMessage(@"
You are a database planner. Return ONLY valid JSON. No markdown backticks. No explanation.

Allowed operations:
- CreateDatabase (databaseName)
- CreateTable (databaseName, tableName, columns)
- CreateDatabaseAndTable (databaseName, tableName, columns)
- ListDatabases ()
- ListTables (databaseName)
- DescribeTable (databaseName, tableName)
- ExecuteQuery (databaseName, query)

Supported column types: INT, BIGINT, BIT, FLOAT, DATE, DATETIME, DECIMAL(18,2), NVARCHAR(50), NVARCHAR(100), NVARCHAR(255), NVARCHAR(MAX), VARCHAR(50), VARCHAR(100), VARCHAR(255).

Example JSON:
{
  ""operation"": ""ListTables"",
  ""databaseName"": ""RedQueenAi""
}");

                planHistory.AddUserMessage(prompt);

                var planResult = await _chatService.GetChatMessageContentAsync(
                    planHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.1,
                        MaxTokens = 500
                    });

                string rawJson = CleanJson(planResult.Content ?? "");
                var plan = JsonSerializer.Deserialize<DataBasePlan>(
                    rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (plan == null)
                    throw new Exception("Could not parse database operation plan.");

                string toolResult = "";

                switch (plan.Operation?.Trim())
                {
                    case "CreateDatabase":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "CreateDatabase",
                            new() { ["databaseName"] = plan.DataBaseName })).ToString();
                        break;

                    case "CreateTable":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "CreateTable",
                            new() { ["databaseName"] = plan.DataBaseName, ["tableName"] = plan.TableName, ["columns"] = plan.Columns })).ToString();
                        break;

                    case "CreateDatabaseAndTable":
                        var dbRes = await _kernel.InvokeAsync("DatabasePlugin", "CreateDatabase",
                            new() { ["databaseName"] = plan.DataBaseName });
                        var tbRes = await _kernel.InvokeAsync("DatabasePlugin", "CreateTable",
                            new() { ["databaseName"] = plan.DataBaseName, ["tableName"] = plan.TableName, ["columns"] = plan.Columns });
                        toolResult = $"{dbRes}\n{tbRes}";
                        break;

                    case "ListDatabases":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "ListDatabases")).ToString();
                        break;

                    case "ListTables":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "ListTables",
                            new() { ["databaseName"] = plan.DataBaseName ?? "RedQueenAi" })).ToString();
                        break;

                    case "DescribeTable":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "DescribeTable",
                            new() { ["databaseName"] = plan.DataBaseName ?? "RedQueenAi", ["tableName"] = plan.TableName })).ToString();
                        break;

                    case "ExecuteQuery":
                        toolResult = (await _kernel.InvokeAsync("DatabasePlugin", "ExecuteSelectQuery",
                            new() { ["databaseName"] = plan.DataBaseName ?? "RedQueenAi", ["selectQuery"] = plan.Query })).ToString();
                        break;

                    default:
                        throw new Exception($"Unsupported database operation: {plan.Operation}");
                }

                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n{toolResult}\n");
                Console.ResetColor();

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(toolResult);

                await _memory.SaveMessageAsync("assistant", toolResult, sessionId);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nDatabase operation failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }

        private static string CleanJson(string text)
        {
            var cleaned = text.Trim();
            if (cleaned.StartsWith("```"))
            {
                var lines = cleaned.Split('\n').ToList();
                if (lines.Count > 0) lines.RemoveAt(0);
                if (lines.Count > 0 && lines[^1].Trim().StartsWith("```")) lines.RemoveAt(lines.Count - 1);
                cleaned = string.Join("\n", lines).Trim();
            }
            return cleaned;
        }

        private static string BuildSearchQuery(string prompt, string lastTopic)
        {
            var cleaned = Regex.Replace(prompt, @"\b(search\s+(the\s+web\s+for|web\s+for|for)?|look\s+up|google|tell\s+me\s+about)\b", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"^(what\s+is|what\s+are|what'?s)\s+", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"\s+(going\s+on|right\s+now|currently)\??$", "", RegexOptions.IgnoreCase).Trim(' ', '?');

            if (string.IsNullOrWhiteSpace(cleaned))
                return prompt;

            return cleaned;
        }

        #endregion
    }
}
