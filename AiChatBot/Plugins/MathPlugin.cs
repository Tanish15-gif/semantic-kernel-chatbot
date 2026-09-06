using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    public class MathPlugins
    {
        [KernelFunction]
        public double Add(int[] numbers)
        {
            Console.WriteLine("Addition Plugin Called");
            return numbers.Sum();
        }

        [KernelFunction]
        public double Subtract(int[] numbers)
        {
            Console.WriteLine("Subtraction Plugin Called");

            if (numbers.Length == 0)
                throw new ArgumentException("No numbers provided");

            double result = numbers[0];

            for (int i = 1; i < numbers.Length; i++)
            {
                result -= numbers[i];
            }

            return result;
        }
        [KernelFunction]
        public double Multiply(int[] numbers)
        {
            Console.WriteLine("Multiplication Plugin Called");
            double product = 1;
            foreach (var num in numbers)
            {
                product *= num;
            }
            return product;
        }
        [KernelFunction]
        public double Divide(int[] numbers)
        {
            Console.WriteLine("Division Plugin Called");
            if (numbers.Length == 0)
                throw new ArgumentException("No numbers provided");


            double div = numbers[0];
            for (int i = 1; i < numbers.Length; i++)
            {
                if (numbers[i] == 0)
                    throw new DivideByZeroException();

                div /= numbers[i];
            }
            return div;
        }
    }
}