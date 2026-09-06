using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    /// <summary>
    /// Provides date and time operations, calculations, and formatting.
    /// </summary>
    public class DatePlugin
    {
        [KernelFunction]
        [Description("Get the current date and time formatted nicely.")]
        public string CurrentDateTime()
        {
            return DateTime.Now.ToString("dddd, dd MMMM yyyy hh:mm:ss tt");
        }

        [KernelFunction]
        [Description("Get only the current date.")]
        public string CurrentDate()
        {
            return DateTime.Now.ToString("dddd, dd MMMM yyyy");
        }

        [KernelFunction]
        [Description("Get only the current time.")]
        public string CurrentTime()
        {
            return DateTime.Now.ToString("hh:mm:ss tt");
        }

        [KernelFunction]
        [Description("Get the current day of the week (e.g. Monday, Tuesday).")]
        public string DayOfWeek()
        {
            return DateTime.Now.DayOfWeek.ToString();
        }

        [KernelFunction]
        [Description("Calculate the number of days between two dates.")]
        public string DaysBetween(string startDate, string endDate)
        {
            if (DateTime.TryParse(startDate, out var start) && DateTime.TryParse(endDate, out var end))
            {
                var span = (end - start).Duration();
                return $"{span.Days} day(s) between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.";
            }
            return "Could not parse one or both dates. Use formats like YYYY-MM-DD or Month DD, YYYY.";
        }

        [KernelFunction]
        [Description("Calculate what date it will be after adding or subtracting a number of days from today.")]
        public string AddDays(int days)
        {
            var target = DateTime.Now.AddDays(days);
            string direction = days >= 0 ? "in " + days + " days" : Math.Abs(days) + " days ago";
            return $"Date {direction}: {target:dddd, dd MMMM yyyy}";
        }
    }
}
