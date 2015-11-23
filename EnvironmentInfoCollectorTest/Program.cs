using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Exceptionless.Logging;
using Exceptionless.Services;

namespace EnvironmentInfoCollectorTest
{
    class Program
    {
        static void Main(string[] args) {
            var test = new EnvironmentInfoCollector(new NullExceptionlessLog());
            var info = test.GetEnvironmentInfo();
            foreach (var property in info.GetType().GetProperties()) {
                Console.WriteLine("{0} : {1}", property.Name, property.GetValue(info));
            }

            Console.ReadKey();
        }
    }
}
