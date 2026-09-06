using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.SemanticKernel.ChatCompletion;
using Ai.Loader;
using Ai.Typer;
using Ai.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Ai.Service;
using Microsoft.Data.SqlClient;

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var dbConnection = config["ConnectionStrings:RedQueenDb"]!;
var memory = new ChatMessageService(dbConnection);
await memory.EnsureTableExistsAsync();

var masterBuilder = new SqlConnectionStringBuilder(dbConnection)
{
    InitialCatalog = "master"
};

string sqlMasterConnection = masterBuilder.ConnectionString;

string apiKey = config["Api:key"]!;

///<Part> In this Part the Method Plugins and other services Are being Registered

var builder = Kernel.CreateBuilder();
builder.Services.AddHttpClient<WebSearchPlugin>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Plugins.AddFromType<WebSearchPlugin>();
builder.Plugins.AddFromType<DatePlugin>();
builder.Plugins.AddFromType<MathPlugins>();
builder.Plugins.AddFromObject(new DatabasePlugin(sqlMasterConnection), "DatabasePlugin");

///If you Didn't Register the Plugins and Services the System couldn't find those kernel Functions.
///</Part>

///<Part> In this Block of code We Initialize Ai with connections 
/// Model Id : The Model of your Ai from where you are getting the Api Key.
/// Api Key  : Take the Api key from the Specific Ai you want like(openrouter.ai,Open Ai etc etc).
/// Endpoint Url : Your Ai Url.
builder.AddOpenAIChatCompletion(
    modelId: "openai/gpt-oss-20b",
    apiKey: apiKey,
    endpoint: new Uri("https://api.groq.com/openai/v1")
);
///</Part>

var kernel = builder.Build();

var chatService = kernel.GetRequiredService<IChatCompletionService>();

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

var oldMessages = await memory.LoadRecentMessageAsync(10);
foreach (var msg in oldMessages)
{
    if (msg.role == "user")
        chathistory.AddUserMessage(msg.Message);
    else if (msg.role == "assistant")
        chathistory.AddAssistantMessage(msg.Message);
}
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine("\t\t\t Welcome to Red Queen Ai\n\n");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("How can i Assist you today?");
Console.ResetColor();

string lastuserTopic = "";

var router = new ToolRouter(kernel, chatService, memory);


while (true)
{
    try
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nYou ➜ ");
        string Prompt = Console.ReadLine()!;
        Console.ResetColor();

        if (Prompt.ToLower() == "exit")
            break;

        await memory.SaveMessageAsync("user", Prompt);

        if (await router.TryHandleAsync(Prompt, chathistory, lastuserTopic))
        {
            if (!router.NeedsSearch(Prompt))
            {
                lastuserTopic = Prompt;
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
                spinnerTask = FetchData.Spinner(
                    cts.Token,
                    "Thinking",
                    ConsoleColor.White
                );

                Console.ForegroundColor = ConsoleColor.Green;

                bool started = false;
                fullResponse = "";

                await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                    chathistory,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.3,
                        MaxTokens = 350,
                        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                    }, kernel))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        if (!started)
                        {
                            cts.Cancel();

                            if (spinnerTask != null)
                                await spinnerTask;

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
                await memory.SaveMessageAsync("assistant", fullResponse);

                break; // success, exit retry loop
            }
            catch (Exception ex) when (ex.Message.Contains("429"))
            {
                retryCount++;

                if (cts != null)
                    cts.Cancel();

                if (spinnerTask != null)
                    await spinnerTask;

                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;

                if (retryCount >= maxRetries)
                {
                    Console.WriteLine($"\nRate limited. Max retries reached.");
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
        Console.WriteLine("Unexpected Error or " + ex.Message);
        Console.ResetColor();
    }
}


