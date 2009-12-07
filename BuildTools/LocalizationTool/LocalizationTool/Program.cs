#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Windows.Forms;

namespace LocalizationTool
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            switch (args[0].ToLower())
            {
                case "clean":
                case "cleanup":
                    Clean(args.Length == 2 ? args[1] : null);
                    break;
                case "build":
                    Compile(args.Length == 2 ? args[1] : null);
                    break;
                case "update":
                    if (args.Length == 2)
                        Update(args[1]);
                    else
                        Update();
                    break;
                case "create":
                    if (args.Length != 2)
                    {
                        Console.WriteLine("missing locale identifier");
                        PrintUsage();
                        return;
                    }
                    Create(args[1]);
                    break;
                default:
                    PrintUsage();
                    return;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: ");

            Console.WriteLine("LocalizationTool.exe CLEAN [locale identifier]");
            Console.WriteLine("LocalizationTool.exe BUILD [locale identifier]");
            Console.WriteLine("LocalizationTool.exe UPDATE [locale identifier]");
            Console.WriteLine("LocalizationTool.exe CREATE <locale indentifier>");
        }

        private static void Update()
        {
            foreach (string bf in System.IO.Directory.GetDirectories(Application.StartupPath))
            {
                string culture = System.IO.Path.GetFileName(bf);

                try
                {
                    System.Globalization.CultureInfo.GetCultureInfo(culture);
                }
                catch
                {
                    continue;
                }

                Update(culture);
            }

        }

        private static void Create(string culture)
        {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.GetCultureInfo(culture); //Validate
            string folder = System.IO.Path.Combine(Application.StartupPath, culture);
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);
            Update(culture);
        }

        private static void Update(string culture)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(System.IO.Path.Combine(Application.StartupPath, "configuration.xml"));

            foreach (System.Xml.XmlNode conf in doc.SelectNodes("root/configuration"))
            {
                string outputfolder = System.IO.Path.GetFullPath(Duplicati.Library.Core.Utility.AppendDirSeperator(System.IO.Path.Combine(Application.StartupPath, culture)));
                string sourcefolder = Duplicati.Library.Core.Utility.AppendDirSeperator(System.IO.Path.GetFullPath(conf["sourcefolder"].InnerText));

                foreach (System.Xml.XmlNode fn in conf.SelectNodes("assembly"))
                {
                    foreach (string s in Duplicati.Library.Core.Utility.EnumerateFiles(System.IO.Path.Combine(sourcefolder, fn.Attributes["folder"].Value)))
                    {
                        if (s.ToLower().StartsWith(Application.StartupPath.ToLower()))
                            continue;

                        if (s.EndsWith(".resx"))
                        {
                            string targetName = System.IO.Path.Combine(outputfolder, s.Substring(sourcefolder.Length));

                            string csFile = System.IO.Path.ChangeExtension(s, ".cs");

                            bool isForm = System.IO.File.Exists(csFile);

                            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(targetName)))
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetName));

                            if (isForm)
                                System.IO.File.Copy(s, targetName, true); //Copy the updated resx

                            targetName = targetName.Substring(0, targetName.Length - "resx".Length) + culture + ".resx";

                            if (System.IO.File.Exists(targetName))
                            {
                                //TODO: Merge
                            }
                            else
                            {
                                if (isForm) //Form
                                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(targetName))
                                        sw.Write(Properties.Resources.Empty_resx);
                                else
                                    System.IO.File.Copy(s, targetName);
                            }

                            System.Xml.XmlDocument doc2 = new System.Xml.XmlDocument();
                            doc2.Load(s);

                            foreach (System.Xml.XmlNode n in doc2.SelectNodes("root/data"))
                                if (n.Attributes["type"] != null && n.Attributes["type"].Value == "System.Resources.ResXFileRef, System.Windows.Forms")
                                {
                                    string relname = n["value"].InnerText;
                                    relname = relname.Substring(0, relname.IndexOf(";"));
                                    string sourceRes = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(s), relname));
                                    string targetRes = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(targetName), relname));
                                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(targetRes)))
                                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetRes));

                                    if (!System.IO.File.Exists(targetRes))
                                        System.IO.File.Copy(sourceRes, targetRes);
                                }

                        }
                    }
                }
            }

        }

        private static void Clean(string cultureReq)
        {
            string root = Duplicati.Library.Core.Utility.AppendDirSeperator(Application.StartupPath);
            if (!string.IsNullOrEmpty(cultureReq))
                root = System.IO.Path.Combine(root, cultureReq);

            foreach (string s in Duplicati.Library.Core.Utility.EnumerateFiles(Application.StartupPath))
                if (System.IO.Path.GetDirectoryName(s) != Application.StartupPath && (System.IO.Path.GetExtension(s) == ".resources" || System.IO.Path.GetExtension(s) == ".dll"))
                    System.IO.File.Delete(s);
                else if (System.IO.Path.GetExtension(s) == ".resx")
                {
                    //Remove the extra files required for resgen.exe
                    string culture = s.Substring(root.Length);
                    culture = culture.Substring(0, culture.IndexOf(System.IO.Path.DirectorySeparatorChar));
                    if (!s.ToLower().EndsWith("." + culture.ToLower() + ".resx") && System.IO.File.Exists(s.Substring(0, s.Length - "resx".Length) + culture + ".resx"))
                        System.IO.File.Delete(s);
                }
                
        }

        private static void Compile(string cultureReq)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(System.IO.Path.Combine(Application.StartupPath, "configuration.xml"));

            foreach (System.Xml.XmlNode conf in doc.SelectNodes("root/configuration"))
            {
                string keyfile = conf["keyfile"] == null ? null : conf["keyfile"].InnerText;
                string versionassembly = conf["versionassembly"].InnerText;
                string outputfolder = conf["outputfolder"].InnerText;
                string productname = conf["productname"].InnerText;

                foreach (System.Xml.XmlNode n in conf.SelectNodes("assembly"))
                {
                    List<string> excludes = new List<string>();
                    foreach (System.Xml.XmlNode x in n.SelectNodes("exclude"))
                        excludes.Add(x.InnerText);


                    string assemblyName = n.Attributes["name"].Value;
                    string folder = n.Attributes["folder"].Value;
                    string @namespace = n.Attributes["namespace"] == null ? assemblyName : n.Attributes["namespace"].Value;

                    foreach (string bf in System.IO.Directory.GetDirectories(Application.StartupPath))
                    {
                        string culture = System.IO.Path.GetFileName(bf);

                        try
                        {
                            System.Globalization.CultureInfo.GetCultureInfo(culture);
                        }
                        catch
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(cultureReq) && !string.Equals(cultureReq, culture, StringComparison.InvariantCultureIgnoreCase))
                            continue;

                        string outfolder = System.IO.Path.GetFullPath(System.IO.Path.Combine(outputfolder, culture));
                        if (!System.IO.Directory.Exists(outfolder))
                            System.IO.Directory.CreateDirectory(outfolder);

                        ResXCompiler.CompileResxFiles(System.IO.Path.Combine(bf, folder), excludes, @namespace, System.IO.Path.Combine(outfolder, assemblyName + ".resources.dll"), System.IO.Path.GetFullPath(versionassembly), keyfile, culture, productname);
                    }
                }
            }
        }
    }
}