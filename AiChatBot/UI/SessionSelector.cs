using Spectre.Console;
using Ai.Service;

namespace Ai.UI
{
    /// <summary>
    /// Displays the Red Queen AI welcome banner and an interactive session picker.
    /// Returns the selected session ID, or:
    ///   -1  → [+] New Chat (create a new session)
    ///   -2  → [ Quit ]   (exit the application)
    /// </summary>
    public static class SessionSelector
    {
        private const string NewChatOption = "[bold green][[+]] New Chat[/]";
        private const string QuitOption    = "[bold red][[ Quit ]][/]";

        public static int Select(List<(int Id, string Title, DateTime CreatedAt)> sessions)
        {
            Console.Clear();

            // ── Banner ────────────────────────────────────────────────────────────────
            AnsiConsole.Write(
                new FigletText("Red Queen AI")
                    .Centered()
                    .Color(Color.Red)
            );

            AnsiConsole.Write(
                new Panel("[bold red]Welcome to Red Queen AI[/]\n[grey]Your concise, tool-powered AI assistant[/]")
                    .BorderColor(Color.DarkRed)
                    .Expand()
                    .RoundedBorder()
            );

            AnsiConsole.WriteLine();

            // ── Build choices ─────────────────────────────────────────────────────────
            var choices = new List<string>();
            choices.Add(NewChatOption);

            foreach (var s in sessions)
            {
                string age   = FormatAge(s.CreatedAt);
                string title = string.IsNullOrWhiteSpace(s.Title) ? "New Chat" : s.Title;
                choices.Add($"[white]{Markup.Escape(title)}[/] [grey]({age})[/]");
            }

            choices.Add(QuitOption);

            var prompt = new SelectionPrompt<string>()
                .Title("[bold yellow]Select a chat session:[/]")
                .PageSize(14)
                .HighlightStyle(new Style(foreground: Color.Red))
                .AddChoices(choices);

            string selected = AnsiConsole.Prompt(prompt);
            AnsiConsole.WriteLine();

            // ── Map selection to return value ─────────────────────────────────────────
            if (selected == NewChatOption)
                return -1;

            if (selected == QuitOption)
                return -2;

            int index = choices.IndexOf(selected) - 1; // -1 because NewChat is index 0
            return sessions[index].Id;
        }

        private static string FormatAge(DateTime createdAt)
        {
            var diff = DateTime.Now - createdAt;

            if (diff.TotalMinutes < 1)  return "just now";
            if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 30) return $"{(int)diff.TotalDays}d ago";
            return createdAt.ToString("MMM d, yyyy");
        }
    }
}
