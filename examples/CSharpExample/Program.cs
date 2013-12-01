using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var settings = new Settings.Settings();
            Console.WriteLine(string.Format("Default settings:\n{0}", settings));
            settings.Load(@"..\..\RuntimeSettings.yaml");
            Console.WriteLine(string.Format("Loaded settings:\n{0}", settings));
            Console.ReadLine();
        }
    }
}
