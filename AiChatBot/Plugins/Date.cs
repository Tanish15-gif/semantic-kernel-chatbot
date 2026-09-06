using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    public class DatePlugin
    {
        [KernelFunction]
        public string CurrentDateTime()
        {
            return DateTime.Now.ToString("dddd, dd MMMM yyyy hh:mm:ss tt");
        }
    }
}