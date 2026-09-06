using System.Text;

namespace Ai.Typer
{
    public class MarkdownStreamRenderer
    {
        private bool _inCodeBlock = false;
        private bool _readingLanguage = false;
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
            {
                WriteChar(ch);
            }
        }

        private void WriteChar(char ch)
        {
            if (_readingLanguage)
            {
                if (ch == '\n')
                {
                    _readingLanguage = false;
                }
                else
                {
                    _language.Append(ch);
                }

                return;
            }

            if (!_inCodeBlock)
            {
                HandleNormalText(ch);
            }
            else
            {
                HandleCodeText(ch);
            }
        }

        private void HandleNormalText(char ch)
        {
            if (ch == '`')
            {
                _normalBackticks++;

                if (_normalBackticks == 3)
                {
                    _inCodeBlock = true;
                    _readingLanguage = true;
                    _normalBackticks = 0;
                    _language.Clear();
                    _codeBuffer.Clear();
                }

                return;
            }

            if (_normalBackticks > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(new string('`', _normalBackticks));
                _normalBackticks = 0;
            }

            if (_lineStart && ch == '#')
            {
                _hashCount++;
                return;
            }

            if (_hashCount > 0)
            {
                if (_hashCount >= 3)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(new string('#', _hashCount));
                }

                _hashCount = 0;
            }

            // Remove ** bold markers
            if (ch == '*')
            {
                _starCount++;

                if (_starCount == 2)
                {
                    _starCount = 0;
                }

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
            if (_normalBackticks > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(new string('`', _normalBackticks));
                Console.ResetColor();
                _normalBackticks = 0;
            }

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