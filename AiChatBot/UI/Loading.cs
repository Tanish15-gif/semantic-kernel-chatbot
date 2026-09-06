namespace Ai.Loader
{
    public class FetchData
    {
        public static async Task Spinner
        (
            CancellationToken token,
            string message = "Thinking",
            ConsoleColor color = ConsoleColor.White
        )
        {
            string[] frames = { "⠋","⠙","⠹","⠸","⠼","⠴","⠦","⠧","⠇","⠏" };
            int i = 0;
            while (!token.IsCancellationRequested)
            {
                Console.ForegroundColor = color;
                Console.Write($"\r{message} {frames[i++ % frames.Length]}");
                Console.ResetColor();

                await Task.Delay(100);
            }
            Console.Write("\r"  + new string(' ',message.Length + 10) + "\r");
        }
    }
}