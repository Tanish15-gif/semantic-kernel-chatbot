namespace Ai.Typer
{
    public static class CodeBlockRenderer
    {
        public static void RenderBox(string code, string language = "")
        {
            var lines = code
                .Replace("\r", "")
                .Split('\n')
                .Select(x => x.TrimEnd())
                .ToList();

            int width = Math.Max(
                40,
                lines.Count > 0 ? lines.Max(x => x.Length) + 4 : 40
            );

            string title = string.IsNullOrWhiteSpace(language) ? "Code" : $"{language.Trim()}";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine();
            Console.Write("╭");
            Console.Write(title);
            Console.WriteLine(new string('─', Math.Max(0, width - title.Length)) + "╮");

            foreach (var line in lines)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;

                string padded = line.PadRight(width - 2);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│ ");
                WriteHighlightedCode(line);
                Console.Write(new string(' ', Math.Max(0, padded.Length - line.Length)));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" │");
            }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╰" + new string('─', width) + "╯");
            Console.ResetColor();
        }
        public static void WriteHighlightedCode(string line)
        {
            string[] keywords =
            {
                "using", "namespace", "class", "public", "private", "static",
                "void", "int", "string", "double", "bool", "var", "new",
                "return", "if", "else", "foreach", "for", "while",
                "async", "await", "try", "catch", "finally"
            };
            var parts = line.Split(' ');

            for (int i = 0; i < parts.Length; i++)
            {
                string words = parts[i];
                if (keywords.Contains(words.Trim()))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }
                else if (words.TrimStart().StartsWith("//"))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                }
                else if (words.Contains("\""))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                }

                Console.Write(words);

                if(i < parts.Length - 1)
                    Console.Write(" ");
            }
        }
    }
}