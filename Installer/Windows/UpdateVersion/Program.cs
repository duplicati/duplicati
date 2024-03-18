// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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
