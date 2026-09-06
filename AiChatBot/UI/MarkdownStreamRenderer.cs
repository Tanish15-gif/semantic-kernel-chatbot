using System.Text;

namespace Ai.Typer
{
    public class MarkdownStreamRenderer
    {
        private bool _inCodeBlock = false;
        private bool _readingLanguage = false;
        private bool _inInlineCode = false;

        private int _normalBackticks = 0;
        private int _codeBackticks = 0;
        private int _hashCount = 0;
        private int _starCount = 0;
        private bool _lineStart = true;

        private readonly StringBuilder _language = new();
        private readonly StringBuilder _codeBuffer = new();

        public void WriteChunk(string chunk)
        {
            foreach (char ch in chunk)
                WriteChar(ch);
        }

        private void WriteChar(char ch)
        {
            if (_readingLanguage)
            {
                if (ch == '\n')
                    _readingLanguage = false;
                else
                    _language.Append(ch);
                return;
            }

            if (!_inCodeBlock)
                HandleNormalText(ch);
            else
                HandleCodeText(ch);
        }

        private void HandleNormalText(char ch)
        {
            if (ch == '`')
            {
                // If we are already in inline code → this is the closing backtick
                if (_inInlineCode)
                {
                    _inInlineCode = false;
                    Console.ResetColor();
                    return;
                }

                _normalBackticks++;

                if (_normalBackticks == 3)
                {
                    // Enter fenced code block
                    _inCodeBlock = true;
                    _readingLanguage = true;
                    _normalBackticks = 0;
                    _language.Clear();
                    _codeBuffer.Clear();
                }

                return;
            }

            // Flush buffered backticks
            if (_normalBackticks > 0)
            {
                if (_normalBackticks == 1)
                {
                    // Single backtick → start of inline code span
                    _inInlineCode = true;
                    _normalBackticks = 0;
                    // Fall through to print the current char inside inline code
                }
                else
                {
                    // 2 literal backticks (edge case) — print as-is
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(new string('`', _normalBackticks));
                    Console.ResetColor();
                    _normalBackticks = 0;
                }
            }

            // Inside inline code → render in cyan, no further formatting
            if (_inInlineCode)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(ch);
                Console.ResetColor();
                return;
            }

            // Heading hashes at line start
            if (_lineStart && ch == '#')
            {
                _hashCount++;
                return;
            }

            if (_hashCount > 0)
            {
                Console.ForegroundColor = _hashCount >= 3
                    ? ConsoleColor.Magenta
                    : ConsoleColor.Green;

                if (_hashCount < 3)
                    Console.Write(new string('#', _hashCount));

                _hashCount = 0;
            }

            // Strip ** bold markers
            if (ch == '*')
            {
                _starCount++;
                if (_starCount == 2)
                    _starCount = 0;
                return;
            }

            if (_starCount > 0)
            {
                Console.Write(new string('*', _starCount));
                _starCount = 0;
            }

            if (ch == '\n')
                _lineStart = true;
            else if (!char.IsWhiteSpace(ch))
                _lineStart = false;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(ch);
            Console.ResetColor();
        }

        private void HandleCodeText(char ch)
        {
            if (ch == '`')
            {
                _codeBackticks++;

                if (_codeBackticks == 3)
                {
                    _inCodeBlock = false;
                    _codeBackticks = 0;

                    CodeBlockRenderer.RenderBox(
                        _codeBuffer.ToString(),
                        _language.ToString()
                    );

                    _codeBuffer.Clear();
                    _language.Clear();
                }

                return;
            }

            if (_codeBackticks > 0)
            {
                _codeBuffer.Append(new string('`', _codeBackticks));
                _codeBackticks = 0;
            }

            _codeBuffer.Append(ch);
        }

        public void Complete()
        {
            // Flush any open inline code
            if (_inInlineCode)
            {
                Console.ResetColor();
                _inInlineCode = false;
            }

            // Flush orphaned backticks
            if (_normalBackticks > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(new string('`', _normalBackticks));
                Console.ResetColor();
                _normalBackticks = 0;
            }

            // Flush unclosed fenced code block
            if (_inCodeBlock && _codeBuffer.Length > 0)
            {
                CodeBlockRenderer.RenderBox(
                    _codeBuffer.ToString(),
                    _language.ToString()
                );

                _codeBuffer.Clear();
                _language.Clear();
                _inCodeBlock = false;
            }

            if (_hashCount > 0)
            {
                Console.Write(new string('#', _hashCount));
                _hashCount = 0;
            }

            if (_starCount > 0)
            {
                Console.Write(new string('*', _starCount));
                _starCount = 0;
            }
        }
    }
}
