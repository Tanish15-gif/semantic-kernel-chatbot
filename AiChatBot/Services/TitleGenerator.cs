using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.RegularExpressions;

namespace Ai.Service
{
    /// <summary>
    /// Generates concise 2-4 word AI titles for chat sessions based on user messages.
    /// </summary>
    public static class TitleGenerator
    {
        public static async Task<string> GenerateTitleAsync(
            IChatCompletionService chatService,
            string firstUserMessage)
        {
            if (string.IsNullOrWhiteSpace(firstUserMessage))
                return "New Chat";

            try
            {
                // Clamp input to 200 chars
                string input = firstUserMessage.Length > 200
                    ? firstUserMessage.Substring(0, 200)
                    : firstUserMessage;

                var history = new ChatHistory();

                history.AddSystemMessage(
                    "You are a chat title generator. Generate a concise 2-4 word title for the conversation based on the user prompt. " +
                    "Return ONLY the title without quotes, markdown, or punctuation."
                );

                history.AddUserMessage(input);

                // openai/gpt-oss-20b is a reasoning model on Groq.
                // It consumes ~60-100 reasoning tokens before producing content,
                // so MaxTokens MUST be at least 250.
                var result = await chatService.GetChatMessageContentAsync(
                    history,
                    new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.3,
                        MaxTokens   = 250
                    }
                );

                string title = (result.Content ?? string.Empty).Trim();

                // Strip surrounding quotes, backticks, punctuation
                title = title.Trim('"', '\'', '.', ',', '!', '?', '-', '`', ':', ';');

                // Take first line only
                var firstLine = title.Split('\n')[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstLine))
                    title = firstLine;

                // Clamp length
                if (title.Length > 60)
                    title = title.Substring(0, 60).Trim();

                if (!string.IsNullOrWhiteSpace(title) && !title.Equals("New Chat", StringComparison.OrdinalIgnoreCase))
                    return title;
            }
            catch
            {
                // Fall back to rule-based title on failure
            }

            return GenerateFallbackTitle(firstUserMessage);
        }

        /// <summary>
        /// Algorithmic fallback that generates a clean 2-4 word title directly from the user's prompt.
        /// Never returns generic "New Chat" if the prompt has words.
        /// </summary>
        public static string GenerateFallbackTitle(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return "New Chat";

            // Remove special characters, keep letters, numbers, and basic tech symbols like +, #
            var cleaned = Regex.Replace(prompt.Trim(), @"[^\w\s\+\#]", " ");
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
                return "New Chat";

            // Skip common filler words at the start
            var filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "i", "want", "to", "can", "you", "please", "how", "do", "what", "is", "are",
                "the", "a", "an", "tell", "me", "about", "give", "write", "provide", "need", "help", "with"
            };

            var meaningful = words.SkipWhile(w => filler.Contains(w)).Take(4).ToList();
            if (meaningful.Count == 0)
                meaningful = words.Take(4).ToList();

            // Capitalize first letter of each word
            var titleWords = meaningful.Select(w =>
                w.Length > 1
                    ? char.ToUpper(w[0]) + w.Substring(1).ToLower()
                    : w.ToUpper());

            string result = string.Join(" ", titleWords).Trim();
            return string.IsNullOrWhiteSpace(result) ? "New Chat" : result;
        }
    }
}
