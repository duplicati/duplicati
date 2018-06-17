using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UpdateVersion
{
    class MainClass
    {
        public static int Main(string[] args)
        {
            args = args ?? new string[0];
            if (args.Length != 2)
            {
                Console.WriteLine("This program updates a .wxi file and sets the version number to the same number as the version code in an executable");
                Console.WriteLine("Usage: {0} <exefile> <wxifile>", Path.GetFileName(Assembly.GetExecutingAssembly().Location));
                return 2;
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine("File not found: {0}", args[0]);
                return 2;
            }

            if (!File.Exists(args[1]))
            {
                Console.WriteLine("File not found: {0}", args[1]);
                return 2;
            }

            var asm = Assembly.ReflectionOnlyLoadFrom(Path.GetFullPath(args[0]));
            var version = asm.GetName().Version;
            Console.WriteLine("Version found: {0}", version);

            var lines = File.ReadAllLines(args[1]);
            var found = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if ((lines[i] ?? string.Empty).Contains("ProductVersion"))
                {
                    var m = new Regex(@"(?<pre>.*ProductVersion\s*=\s*\"")(?<version>[^\""]+)(?<post>\"".*)").Match(lines[i]);
                    lines[i] = m.Groups["pre"] + version.ToString(4) + m.Groups["post"];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.WriteLine("No ProductVersion tag found in {0}", args[1]);
                return 2;
            }

            File.WriteAllLines(args[1], lines);
            Console.WriteLine("Updated {0} with verison {1}", args[1], version.ToString(4));
            return 0;
        }
    }
}
