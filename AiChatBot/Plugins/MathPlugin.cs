using System.ComponentModel;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    /// <summary>
    /// Provides mathematical calculations, scientific functions, and expression evaluation.
    /// </summary>
    public class MathPlugins
    {
        [KernelFunction]
        [Description("Evaluate a mathematical expression, supporting +, -, *, /, %, parentheses, and decimals. Example: '(15 * 4) + (100 / 5) - 2.5'")]
        public string EvaluateExpression(string expression)
        {
            try
            {
                // Clean and normalize expression
                string expr = expression.Replace("x", "*").Replace("X", "*").Replace("÷", "/");

                // Handle power operator ^ (e.g. 2^8 -> Math.Pow(2,8))
                expr = Regex.Replace(expr, @"(\d+(\.\d+)?)\s*\^\s*(\d+(\.\d+)?)", match =>
                {
                    double b = double.Parse(match.Groups[1].Value);
                    double e = double.Parse(match.Groups[3].Value);
                    return Math.Pow(b, e).ToString(System.Globalization.CultureInfo.InvariantCulture);
                });

                // Safe evaluation via DataTable Compute
                var table = new DataTable();
                var result = table.Compute(expr, null);
                double val = Convert.ToDouble(result);
                return Math.Round(val, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                return $"Error evaluating expression: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Add an array of decimal or integer numbers together.")]
        public double Add(double[] numbers)
        {
            if (numbers == null || numbers.Length == 0) return 0;
            return Math.Round(numbers.Sum(), 6);
        }

        [KernelFunction]
        [Description("Subtract numbers sequentially (first - second - third...).")]
        public double Subtract(double[] numbers)
        {
            if (numbers == null || numbers.Length == 0)
                throw new ArgumentException("No numbers provided");

            double result = numbers[0];
            for (int i = 1; i < numbers.Length; i++)
                result -= numbers[i];

            return Math.Round(result, 6);
        }

        [KernelFunction]
        [Description("Multiply an array of decimal or integer numbers together.")]
        public double Multiply(double[] numbers)
        {
            if (numbers == null || numbers.Length == 0) return 0;
            double product = 1;
            foreach (var num in numbers)
                product *= num;

            return Math.Round(product, 6);
        }

        [KernelFunction]
        [Description("Divide numbers sequentially (first / second / third...).")]
        public double Divide(double[] numbers)
        {
            if (numbers == null || numbers.Length == 0)
                throw new ArgumentException("No numbers provided");

            double div = numbers[0];
            for (int i = 1; i < numbers.Length; i++)
            {
                if (Math.Abs(numbers[i]) < 1e-12)
                    throw new DivideByZeroException("Cannot divide by zero.");
                div /= numbers[i];
            }

            return Math.Round(div, 6);
        }

        [KernelFunction]
        [Description("Calculate power (base raised to the exponent power).")]
        public double Power(double baseNumber, double exponent)
        {
            return Math.Round(Math.Pow(baseNumber, exponent), 6);
        }

        [KernelFunction]
        [Description("Calculate the square root of a non-negative number.")]
        public double SquareRoot(double number)
        {
            if (number < 0)
                throw new ArgumentException("Cannot calculate square root of a negative number.");
            return Math.Round(Math.Sqrt(number), 6);
        }

        [KernelFunction]
        [Description("Calculate percentage (e.g. 20% of 150 = 30).")]
        public double Percentage(double percentage, double total)
        {
            return Math.Round((percentage / 100.0) * total, 6);
        }

        [KernelFunction]
        [Description("Calculate remainder/modulus of a divided by b (a % b).")]
        public double Modulus(double a, double b)
        {
            if (Math.Abs(b) < 1e-12)
                throw new DivideByZeroException("Cannot calculate modulus with zero divisor.");
            return Math.Round(a % b, 6);
        }
    }
}
