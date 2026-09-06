using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using Ai.Loader;
using Ai.Typer;
using Ai.Plugins;
using Ai.Service;
using Ai.UI;
using Microsoft.Data.SqlClient;

// ─── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var dbConnection = config["ConnectionStrings:RedQueenDb"]!;
string apiKey = config["Api:key"]!;

// ─── SQL connection for plugins ────────────────────────────────────────────────
var masterBuilder = new SqlConnectionStringBuilder(dbConnection)
{
    InitialCatalog = "master"
};
string sqlMasterConnection = masterBuilder.ConnectionString;

// ─── Build Semantic Kernel (once, shared across sessions) ─────────────────────
var builder = Kernel.CreateBuilder();

builder.Services.AddHttpClient<WebSearchPlugin>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    CookieContainer = new System.Net.CookieContainer(),
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});
builder.Plugins.AddFromType<WebSearchPlugin>();
builder.Plugins.AddFromType<DatePlugin>();
builder.Plugins.AddFromType<MathPlugins>();
builder.Plugins.AddFromObject(new DatabasePlugin(sqlMasterConnection), "DatabasePlugin");

/// Model Id : The AI model from your provider.
/// Api Key  : From openrouter.ai, Groq, OpenAI, etc.
/// Endpoint : The provider's API URL.
builder.AddOpenAIChatCompletion(
    modelId: "openai/gpt-oss-20b",
    apiKey: apiKey,
    endpoint: new Uri("https://api.groq.com/openai/v1")
);

var kernel = builder.Build();
var chatService = kernel.GetRequiredService<IChatCompletionService>();

// ─── Database services (once, shared across sessions) ─────────────────────────
var memory = new ChatMessageService(dbConnection);
var sessionSvc = new ChatSessionService(dbConnection);

await memory.EnsureTableExistsAsync();
await sessionSvc.EnsureTablesExistAsync();

// Automatically repair any legacy untitled sessions in database
await sessionSvc.RepairUntitledSessionsAsync(chatService);

// ─── Tool router (once, shared across sessions) ───────────────────────────────
var router = new ToolRouter(kernel, chatService, memory);

Task? activeTitleTask = null;

// ══════════════════════════════════════════════════════════════════════════════
// OUTER LOOP — Session selection. Runs every time the user types 'exit'.
// ══════════════════════════════════════════════════════════════════════════════
while (true)
{
    // Ensure any background title generation from the previous session completes
    if (activeTitleTask != null && !activeTitleTask.IsCompleted)
    {
        await Task.WhenAny(activeTitleTask, Task.Delay(2500));
    }

    // ── Show session picker ────────────────────────────────────────────────────
    var sessions = await sessionSvc.GetAllSessionsAsync();
    int sessionId = SessionSelector.Select(sessions);

    // sessionId == -1  → user picked "[+] New Chat"
    // sessionId == -2  → user picked "[ Quit ]"
    if (sessionId == -2)
        break;

    bool isNewSession = sessionId == -1;
    if (isNewSession)
        sessionId = await sessionSvc.CreateSessionAsync();

    // ── Build fresh ChatHistory for this session ───────────────────────────────
    var chathistory = new ChatHistory();

    chathistory.AddSystemMessage(@"You are 'Red Queen AI', a concise, fast, and token-efficient AI assistant.
You remember context from previous conversation history.
You have access to tools whenever needed for calculations, web lookups, date/time, or database tasks.

CRITICAL INSTRUCTIONS FOR BREVITY:
- Give direct, minimal, and punchy answers respecting token limits.
- Do NOT write long essays, wordy introductions, or unnecessary disclaimers.
- Get straight to the answer without conversational filler or repetition.
- Provide 1 to 3 concise sentences or a brief focused bullet list unless the user explicitly requests deep explanations.
- Combine information efficiently and stay concise at all times.");

    var oldMessages = await memory.LoadRecentMessageAsync(sessionId, 10);
    foreach (var msg in oldMessages)
    {
        if (msg.role == "user")
            chathistory.AddUserMessage(msg.Message);
        else if (msg.role == "assistant")
            chathistory.AddAssistantMessage(msg.Message);
    }

    // ── Session header ─────────────────────────────────────────────────────────
    var currentSession = sessions.FirstOrDefault(s => s.Id == sessionId);
    string sessionLabel = isNewSession ? "New Chat" : (currentSession.Title ?? "New Chat");

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n  ── Session: {sessionLabel} ──\n");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  Type 'exit' to go back to session list  |  'quit' to close the app\n");
    Console.ResetColor();

    string lastuserTopic = "";
    bool titleGenerated = !isNewSession; // Only generate title for new sessions

    // ════════════════════════════════════════════════════════════════════════
    // INNER LOOP — Chat REPL for the active session.
    // 'exit' breaks this loop → returns to session picker.
    // 'quit' breaks BOTH loops → closes the app.
    // ════════════════════════════════════════════════════════════════════════
    bool quitApp = false;

    while (true)
    {
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("You ➜ ");
            string Prompt = Console.ReadLine()!;
            Console.ResetColor();

            if (string.IsNullOrWhiteSpace(Prompt))
                continue;

            // 'exit' → go back to session picker
            if (Prompt.Trim().ToLower() == "exit")
            {
                if (activeTitleTask != null && !activeTitleTask.IsCompleted)
                {
                    await Task.WhenAny(activeTitleTask, Task.Delay(2500));
                }
                break;
            }

            // 'quit' → fully close the app
            if (Prompt.Trim().ToLower() == "quit")
            {
                quitApp = true;
                break;
            }

            await memory.SaveMessageAsync("user", Prompt, sessionId);

            // Immediately set a smart fallback title for new sessions so it never stays "New Chat"
            if (isNewSession && !titleGenerated)
            {
                string quickTitle = TitleGenerator.GenerateFallbackTitle(Prompt);
                await sessionSvc.UpdateSessionTitleAsync(sessionId, quickTitle);
            }

            if (await router.TryHandleAsync(Prompt, chathistory, lastuserTopic, sessionId))
            {
                if (!router.NeedsSearch(Prompt))
                    lastuserTopic = Prompt;

                // Refine session title using LLM after the first handled reply
                if (!titleGenerated)
                {
                    titleGenerated = true;
                    string capturedPrompt = Prompt;
                    int capturedId = sessionId;
                    activeTitleTask = Task.Run(async () =>
                    {
                        try
                        {
                            string title = await TitleGenerator.GenerateTitleAsync(chatService, capturedPrompt);
                            if (!string.IsNullOrWhiteSpace(title) && !title.Equals("New Chat", StringComparison.OrdinalIgnoreCase))
                            {
                                await sessionSvc.UpdateSessionTitleAsync(capturedId, title);
                            }
                        }
                        catch { /* non-fatal */ }
                    });
                }

                continue;
            }

            lastuserTopic = Prompt;
            chathistory.AddUserMessage(Prompt);

            string fullResponse = "";
            int retryCount = 0;
            int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                CancellationTokenSource? cts = null;
                Task? spinnerTask = null;

                try
                {
                    var renderer = new MarkdownStreamRenderer();

                    cts = new CancellationTokenSource();
                    spinnerTask = FetchData.Spinner(cts.Token, "Thinking", ConsoleColor.White);

                    Console.ForegroundColor = ConsoleColor.Green;

                    bool started = false;
                    fullResponse = "";

                    await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                        chathistory,
                        new OpenAIPromptExecutionSettings
                        {
                            Temperature = 0.3,
                            MaxTokens = 600,
                            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                        }, kernel))
                    {
                        if (!string.IsNullOrEmpty(chunk.Content))
                        {
                            if (!started)
                            {
                                cts.Cancel();
                                if (spinnerTask != null) await spinnerTask;
                                Console.Write("\r                \r");
                                started = true;
                            }

                            foreach (char c in chunk.Content)
                            {
                                renderer.WriteChunk(c.ToString());
                                fullResponse += c;
                                await Task.Delay(0);
                            }
                        }
                    }

                    renderer.Complete();
                    Console.ResetColor();
                    Console.WriteLine("\n");

                    chathistory.AddAssistantMessage(fullResponse);
                    await memory.SaveMessageAsync("assistant", fullResponse, sessionId);

                    // Refine session title using LLM after the first general chat reply
                    if (!titleGenerated)
                    {
                        titleGenerated = true;
                        string capturedPrompt = Prompt;
                        int capturedId = sessionId;
                        activeTitleTask = Task.Run(async () =>
                        {
                            try
                            {
                                string title = await TitleGenerator.GenerateTitleAsync(chatService, capturedPrompt);
                                if (!string.IsNullOrWhiteSpace(title) && !title.Equals("New Chat", StringComparison.OrdinalIgnoreCase))
                                {
                                    await sessionSvc.UpdateSessionTitleAsync(capturedId, title);
                                }
                            }
                            catch { /* non-fatal */ }
                        });
                    }

                    break; // success — exit retry loop
                }
                catch (Exception ex) when (ex.Message.Contains("429"))
                {
                    retryCount++;

                    if (cts != null) cts.Cancel();
                    if (spinnerTask != null) await spinnerTask;

                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    if (retryCount >= maxRetries)
                    {
                        Console.WriteLine("\nRate limited. Max retries reached.");
                        Console.ResetColor();
                        break;
                    }

                    Console.WriteLine($"\nRate limited. Retrying {retryCount}/{maxRetries} in 10 seconds...");
                    Console.ResetColor();
                    await Task.Delay(10000);
                }
                finally
                {
                    if (cts != null && !cts.IsCancellationRequested)
                        cts.Cancel();

                    Console.ResetColor();
                }
            }

            if (string.IsNullOrWhiteSpace(fullResponse))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nAI did not respond. Try again after some time.");
                Console.ResetColor();
            }
        }
        catch (Exception ex) when (ex.Message.Contains("429"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nRate limited — waiting 10s before retry...");
            Console.ResetColor();
        }
        catch (Exception ex) when (ex.Message.Contains("413") || ex.Message.Contains("too large"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nRequest too large — trimming oldest messages and retrying.");
            Console.ResetColor();

            var systemMsg = chathistory.First();
            var rest = chathistory.Skip(1).Skip(5).ToList();
            chathistory.Clear();
            chathistory.Add(systemMsg);
            foreach (var m in rest) chathistory.Add(m);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Unexpected Error: " + ex.Message);
            Console.ResetColor();
        }
    }

    // If the user typed 'quit', exit the outer loop too
    if (quitApp)
        break;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("\n  Returning to session list...\n");
    Console.ResetColor();
}

Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine("\n  Goodbye from Red Queen AI.\n");
Console.ResetColor();
