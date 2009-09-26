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
