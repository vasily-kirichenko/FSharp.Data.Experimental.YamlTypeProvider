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
            //settings.Changed += (obj, __) => Console.WriteLine("Root changed!"); //\nNew:\n{0}", obj);
            settings.Mail.Changed += (obj, __) => Console.WriteLine("Mail changed!"); //\nNew:\n{0}", obj);
            settings.Mail.Pop3.Changed += (obj, __) => Console.WriteLine("Pop3 changed!"); //\nNew:\n{0}", obj);
            Console.WriteLine(string.Format("Default settings:\n{0}", settings));
            settings.LoadAndWatch(@"..\..\RuntimeSettings.yaml");
            Console.WriteLine(string.Format("Loaded settings:\n{0}", settings));
            Console.ReadLine();
        }
    }
}
