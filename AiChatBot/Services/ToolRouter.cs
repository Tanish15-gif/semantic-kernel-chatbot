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
        public ToolRouter
        (
            Kernel kernel,
            IChatCompletionService chatservice,
            ChatMessageService memory
        )
        {
            _kernel = kernel;
            _chatService = chatservice;
            _memory = memory;
        }

        public async Task<bool> TryHandleAsync(string prompt, ChatHistory chatHistory, string lastuserTopic)
        {
            if (NeedsDatabaseOperation(prompt))
            {
                await HandleDatabaseAsync(prompt, chatHistory);
                return true;
            }
            if (NeedsDateTime(prompt))
            {
                await HandleDateTimeAsync(prompt, chatHistory);
                return true;
            }
            if (NeedsMath(prompt))
            {
                await HandleMathAsync(prompt, chatHistory);
                return true;
            }


            if (NeedsSearch(prompt))
            {
                await HandleSearchAsync(prompt, chatHistory, lastuserTopic);
                return true;
            }

            return false;
        }
        public bool NeedsSearch(string prompt)
        {
            string p = prompt.ToLower();

            return p.Contains("latest") ||
                   p.Contains("news") ||
                   p.Contains("today") ||
                   p.Contains("current") ||
                   p.Contains("recent") ||
                   p.Contains("released") ||
                   p.Contains("release") ||
                   p.Contains("2026") ||
                   p.Contains("new");
        }
        public bool NeedsDateTime(string prompt)
        {
            string p = prompt.ToLower();

            return p.Contains("time") ||
                p.Contains("date") ||
                p.Contains("day");
        }
        public bool NeedsMath(string prompt)
        {
            string p = prompt.ToLower();

            if (p.Contains("c++") || p.Contains("c#") || p.Contains("f#"))
                return false;

            bool hasMathExpression = Regex.IsMatch(
                p, @"\d+\s*[\+\-\*/]\s*\d+"
            );

            bool hasMathWords =
        p.Contains("add") ||
        p.Contains("plus") ||
        p.Contains("sum") ||
        p.Contains("subtract") ||
        p.Contains("minus") ||
        p.Contains("multiply") ||
        p.Contains("times") ||
        p.Contains("divide") ||
        p.Contains("division");

            bool hasNumber = Regex.IsMatch(p, @"\d");

            return hasMathExpression || (hasMathWords && hasNumber);
        }

        public bool NeedsDatabaseOperation(string prompt)
        {
            string p = prompt.ToLower();

            if (p.StartsWith("how to") ||
        p.StartsWith("how can") ||
        p.StartsWith("what is") ||
        p.StartsWith("explain") ||
        p.StartsWith("teach") ||
        p.Contains("how do i"))
            {
                return false;
            }

            return p.Contains("create database") ||
                   p.Contains("create a database") ||
                   p.Contains("make database") ||
                   p.Contains("make a database") ||
                   p.Contains("new database") ||
                   p.Contains("create table") ||
                   p.Contains("create a table") ||
                   p.Contains("make table") ||
                   p.Contains("make a table") ||
                   (p.Contains("database") && p.Contains("table"));
        }
        private async Task HandleMathAsync(string Prompt, ChatHistory chatHistory)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Calculating", ConsoleColor.Cyan);


            string answer = "";
            var renderer = new MarkdownStreamRenderer();

            try
            {
                string lowerPrompt = Prompt.ToLower();
                int[] numbers = ExtractNumbers(Prompt);

                if (numbers.Length == 0)
                {
                    cts.Cancel();
                    await spinnerTask;

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\nNo numbers found for calculation.\n");
                    Console.ResetColor();
                    return;
                }

                if (lowerPrompt.Contains("subtract") && lowerPrompt.Contains("from") && numbers.Length == 2)
                {
                    numbers = new[] { numbers[1], numbers[0] };
                }
                var toolResult = lowerPrompt switch
                {
                    var p when p.Contains("add") || p.Contains("plus") || p.Contains("sum") || p.Contains("+")
                        => await _kernel.InvokeAsync("MathPlugins", "Add", new() { ["numbers"] = numbers }),

                    var p when p.Contains("subtract") || p.Contains("minus") || p.Contains("-")
                        => await _kernel.InvokeAsync("MathPlugins", "Subtract", new() { ["numbers"] = numbers }),

                    var p when p.Contains("multiply") || p.Contains("times") || p.Contains("*")
                        => await _kernel.InvokeAsync("MathPlugins", "Multiply", new() { ["numbers"] = numbers }),

                    _ => await _kernel.InvokeAsync("MathPlugins", "Divide", new() { ["numbers"] = numbers })
                };
                cts.Cancel();
                await spinnerTask;
                var toolHistory = new ChatHistory();

                toolHistory.AddSystemMessage(@"
                    You are Red Queen AI.
                    The user asked a math question.
                    State the direct answer concisely in 1 sentence or plain result.
                    No filler or lengthy explanation.
                ");

                toolHistory.AddUserMessage($@"
                User question:
                {Prompt}

                Tool result:
                {toolResult}
                ");

                Console.ForegroundColor = ConsoleColor.Green;

                await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                    toolHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.1,
                        MaxTokens = 80
                    }
                ))
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
                Console.WriteLine("\n");

                chatHistory.AddUserMessage(Prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nMath plugin failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }

        private async Task HandleDateTimeAsync(string prompt, ChatHistory chatHistory)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Checking Time", ConsoleColor.Blue);

            string answer = "";
            var renderer = new MarkdownStreamRenderer();

            try
            {
                var toolResult = await _kernel.InvokeAsync(
                    "DatePlugin", "CurrentDateTime"
                );
                cts.Cancel();
                await spinnerTask;

                var toolHistory = new ChatHistory();

                toolHistory.AddSystemMessage(@"
                    You are Red Queen AI.
                    The user asked about date or time.
                    State the answer directly in 1 brief, natural sentence.
                    No filler.
                ");
                toolHistory.AddUserMessage($@"
                    User question:
                    {prompt}

                    Tool result:
                    {toolResult}
                ");
                Console.ForegroundColor = ConsoleColor.Green;

                await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                    toolHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.2,
                        MaxTokens = 60
                    }
                ))
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
                Console.WriteLine("\n");

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer);
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nDate plugin failed: {ex.Message}\n");
                Console.ResetColor();
            }
        }
        private async Task HandleSearchAsync(
            string prompt,
            ChatHistory chatHistory,
            string lastUserTopic)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(cts.Token, "Searching Web", ConsoleColor.Yellow);

            string answer = "";
            var renderer = new MarkdownStreamRenderer();

            try
            {
                string searchQuery = BuildSearchQuery(prompt, lastUserTopic);

                var searchResults = await _kernel.InvokeAsync(
                    "WebSearchPlugin",
                    "Search",
                    new() { ["query"] = searchQuery }
                );

                var researchHistory = new ChatHistory();

                researchHistory.AddSystemMessage(@"
You are a concise browsing assistant.
Use ONLY the provided search results and page content.
Do not invent details.

Rules for response:
- Provide a direct, minimal answer (2 to 4 sentences maximum).
- Focus strictly on answering the user's question directly.
- Do NOT include long essays, filler intros, or repeated points.
- If the exact answer isn't in the sources, state that clearly in one sentence.
");

                researchHistory.AddUserMessage($@"
User question:
{prompt}

Search query used:
{searchQuery}

Search results:
{searchResults}
");

                cts.Cancel();
                await spinnerTask;

                Console.ForegroundColor = ConsoleColor.Green;

                await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                    researchHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.3,
                        MaxTokens = 250
                    }
                ))
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
                Console.WriteLine("\n");

                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer);
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

        private static string BuildSearchQuery(string prompt, string lastTopic)
        {
            string p = prompt.ToLower();

            bool isFollowUp =
                p.Contains("latest") ||
                p.Contains("new") ||
                p.Contains("current") ||
                p.Contains("today") ||
                p.Contains("this") ||
                p.Contains("that") ||
                p.Contains("it");

            if (isFollowUp && !string.IsNullOrWhiteSpace(lastTopic))
            {
                return $"{lastTopic} {prompt}";
            }

            return prompt;
        }
        private static int[] ExtractNumbers(string text)
        {
            return System.Text.RegularExpressions.Regex
                .Matches(text, @"-?\d+")
                .Select(m => int.Parse(m.Value))
                .ToArray();
        }
        private async Task HandleDatabaseAsync(string prompt, ChatHistory chatHistory)
        {
            var cts = new CancellationTokenSource();
            var spinnerTask = FetchData.Spinner(
                cts.Token,
                "Executing SQL",
                ConsoleColor.Blue
            );

            string answer = "";
            var renderer = new MarkdownStreamRenderer();

            try
            {
                var planHistory = new ChatHistory();

                planHistory.AddSystemMessage(@"
You are a database operation planner.

Return ONLY valid JSON.
No markdown.
No explanation.

Allowed operations:
1. CreateDatabase
2. CreateTable
3. CreateDatabaseAndTable

Rules:
- Use safe SQL Server types only:
  INT, BIGINT, BIT, FLOAT, DATE, DATETIME,
  DECIMAL(18,2),
  NVARCHAR(50), NVARCHAR(100), NVARCHAR(255), NVARCHAR(MAX),
  VARCHAR(50), VARCHAR(100), VARCHAR(255)
- If user does not give column types, infer sensible types.
- If user asks for ProductId/Id primary key, make it INT identity primary key.
- DatabaseName and TableName must be simple names only.

JSON shape:
{
  ""operation"": ""CreateDatabaseAndTable"",
  ""databaseName"": ""ShopDB"",
  ""tableName"": ""Products"",
  ""columns"": [
    {
      ""name"": ""ProductId"",
      ""type"": ""INT"",
      ""isPrimaryKey"": true,
      ""isIdentity"": true,
      ""isNullable"": false
    }
  ]
}
");

                planHistory.AddUserMessage(prompt);

                var planResult = await _chatService.GetChatMessageContentAsync(
                    planHistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.1,
                        MaxTokens = 350
                    }
                );

                string rawJson = CleanJson(planResult.Content ?? "");

                var plan = JsonSerializer.Deserialize<DataBasePlan>(
                    rawJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                if (plan == null)
                    throw new Exception("AI did not return a valid database plan.");

                string toolResult = "";

                if (plan.Operation == "CreateDatabase")
                {
                    var result = await _kernel.InvokeAsync(
                        "DatabasePlugin",
                        "CreateDatabase",
                        new() { ["databaseName"] = plan.DataBaseName }
                    );

                    toolResult = result.ToString();
                }
                else if (plan.Operation == "CreateTable")
                {
                    var result = await _kernel.InvokeAsync(
                        "DatabasePlugin",
                        "CreateTable",
                        new()
                        {
                            ["databaseName"] = plan.DataBaseName,
                            ["tableName"] = plan.TableName,
                            ["columns"] = plan.Columns
                        }
                    );

                    toolResult = result.ToString();
                }
                else if (plan.Operation == "CreateDatabaseAndTable")
                {
                    var dbResult = await _kernel.InvokeAsync(
                        "DatabasePlugin",
                        "CreateDatabase",
                        new() { ["databaseName"] = plan.DataBaseName }
                    );

                    var tableResult = await _kernel.InvokeAsync(
                        "DatabasePlugin",
                        "CreateTable",
                        new()
                        {
                            ["databaseName"] = plan.DataBaseName,
                            ["tableName"] = plan.TableName,
                            ["columns"] = plan.Columns
                        }
                    );

                    toolResult = dbResult + "\n" + tableResult;
                }
                else
                {
                    throw new Exception($"Unsupported database operation: {plan.Operation}");
                }

                cts.Cancel();
                await spinnerTask;

                answer = toolResult;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n{answer}\n");
                Console.ResetColor();
                chatHistory.AddUserMessage(prompt);
                chatHistory.AddAssistantMessage(answer);

                await _memory.SaveMessageAsync("assistant", answer);
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
            text = text.Trim();

            if (text.StartsWith("```"))
            {
                text = text
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');

            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1);

            return text;
        }
    }
}