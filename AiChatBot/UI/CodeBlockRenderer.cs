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

            // Strip leading/trailing blank lines
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            // Calculate separator width (clamped to console width - 2)
            int maxLineLen = lines.Count > 0 ? lines.Max(x => x.Length) : 0;
            int consoleWidth = Math.Max(40, Console.WindowWidth - 2);
            int width = Math.Min(Math.Max(40, maxLineLen + 6), consoleWidth);

            string title = string.IsNullOrWhiteSpace(language) ? "code" : language.Trim().ToLower();
            string titlePad = $" {title} ";

            // ── Top separator: ─── language ──────────────────────
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            int dashesAfter = Math.Max(0, width - titlePad.Length - 3);
            Console.Write("  ──" + titlePad);
            Console.WriteLine(new string('─', dashesAfter));

            // ── Code lines (2-space indent, no side borders)
            foreach (var line in lines)
            {
                Console.Write("  "); // 2-space indent only — no │ border
                WriteHighlightedCode(line);
                Console.WriteLine();
            }

            // ── Bottom separator: ────────────────────────────────
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  " + new string('─', width));
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void WriteHighlightedCode(string line)
        {
            string[] keywords =
            {
                "using", "namespace", "class", "public", "private", "protected",
                "static", "void", "int", "string", "double", "float", "bool",
                "var", "new", "null", "true", "false", "return",
                "if", "else", "foreach", "for", "while", "do",
                "async", "await", "try", "catch", "finally", "throw",
                // SQL keywords
                "SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER",
                "TABLE", "DATABASE", "FROM", "WHERE", "JOIN", "ON", "INTO",
                "VALUES", "PRIMARY", "KEY", "IDENTITY", "NOT", "NULL", "DEFAULT",
                "INT", "NVARCHAR", "VARCHAR", "DATETIME", "BIGINT", "BIT",
                "ORDER", "BY", "GROUP", "HAVING", "INNER", "LEFT", "RIGHT",
                "WITH", "AS", "BEGIN", "END", "IF", "EXISTS", "SET"
            };

            var parts = line.Split(' ');

            for (int i = 0; i < parts.Length; i++)
            {
                string word = parts[i];

                if (keywords.Contains(word.Trim().TrimEnd('(', ')', ',', ';')))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }
                else if (word.TrimStart().StartsWith("--") || word.TrimStart().StartsWith("//"))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    // Print rest of line as comment
                    Console.Write(string.Join(" ", parts.Skip(i)));
                    break;
                }
                else if (word.Contains('"') || word.Contains('\''))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                }

                Console.Write(word);
                if (i < parts.Length - 1)
                    Console.Write(" ");
            }

            Console.ResetColor();
        }
    }
}
