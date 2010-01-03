#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace ResXEscapeCodeFixer
{
    class Program
    {
        static void Main(string[] args)
        {
            string startpath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (args.Length >= 1)
                startpath = args[0];

            Console.WriteLine("Root folder is: {0}", startpath);

            Queue<string> folders = new Queue<string>();
            folders.Enqueue(startpath);

            while (folders.Count > 0)
            {
                string folder = folders.Dequeue();

                Console.WriteLine("Processing folder: {0}", folder);

                foreach (string s in Directory.GetDirectories(folder))
                    folders.Enqueue(s);

                foreach (string s in Directory.GetFiles(folder, "*.resx"))
                    FixUpResX(s);

            }
                
        }

        static void FixUpResX(string file)
        {
            bool modified = false;
            Console.WriteLine("Examining file: {0}", Path.GetFileName(file));
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(file);

            foreach (System.Xml.XmlNode n in doc.SelectNodes("root/data"))
            {
                //TODO: Make sure that xml:space="preserve" is present
                System.Xml.XmlNode value = n["value"];
                if (value != null)
                {
                    if (value.InnerText.IndexOf('\\') > 0)
                    {
                        string newvalue = value.InnerText
                            .Replace("\\r\\n", "\r\n")
                            .Replace("\\n", "\r\n")
                            .Replace("\\r", "\r")
                            .Replace("\\t", "\t")
                            .Replace("\\\"", "\"");
                        if (newvalue != value.InnerText)
                        {
                            Console.WriteLine("Replacing: \r\n\t{0}\r\n\t{1}", value.InnerText, newvalue);
                            value.InnerText = newvalue;
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
                doc.Save(file);
        }
    }
}
