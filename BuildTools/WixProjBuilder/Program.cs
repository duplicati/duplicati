#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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

namespace WixProjBuilder
{
    class Program
    {
        static void Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.CommandLine.CommandLineParser.ExtractOptions(args);

            if (args.Count != 1)
            {
                Console.WriteLine("Usage: ");
                Console.WriteLine("  WixProjBuilder.exe <projfile> [option=value]");
                return;
            }

            if (!System.IO.File.Exists(args[0]))
            {
                Console.WriteLine(string.Format("File not found: {0}", args[0]));
                return;
            }

            string wixpath;
            if (options.ContainsKey("wixpath"))
                wixpath = options["wixpath"];
            else
                wixpath = System.Environment.GetEnvironmentVariable("WIXPATH");

            if (string.IsNullOrEmpty(wixpath))
            {
                string[] known_wix_names = new string[] 
                {
                    "Windows Installer XML v3",
                    "Windows Installer XML v3.1",
                    "Windows Installer XML v3.2",
                    "Windows Installer XML v3.3",
                    "Windows Installer XML v3.4",
                    "Windows Installer XML v3.5",
                    "Windows Installer XML v3.6",
                    "Windows Installer XML v3.7",
                    "Windows Installer XML v3.8",
                    "Windows Installer XML v3.9",
                    "WiX Toolset v3.6",
                    "WiX Toolset v3.7",
                    "WiX Toolset v3.8",
                    "WiX Toolset v3.9",
                };

                foreach(var p in known_wix_names)
                {
                    foreach(var p2 in new string[] {"%ProgramFiles(x86)%", "%programfiles%"})
                    {
                        wixpath = System.IO.Path.Combine(System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(p2), p), "bin");
                        if (System.IO.Directory.Exists(wixpath))
                        {
                            Console.WriteLine(string.Format("*** wixpath not specified, using: {0}", wixpath));
                            break;
                        }
                    }

                    if (System.IO.Directory.Exists(wixpath))
                        break;
                    }

            }


            if (!System.IO.Directory.Exists(wixpath))
            {
                Console.WriteLine(string.Format("WiX not found, please install Windows Installer XML 3 or greater"));
                Console.WriteLine(string.Format("  supply path to WiX bin folder with option --wixpath=...path..."));
                return;
            }

            args[0] = System.IO.Path.GetFullPath(args[0]);

            Console.WriteLine(string.Format("Parsing wixproj file: {0}", args[0]));

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(args[0]);

            string config = "Release";
            if (options.ContainsKey("configuration"))
                config = options["configuration"];

            string projdir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0]));

            List<string> includes = new List<string>();
            List<string> refs = new List<string>();
            List<string> content = new List<string>();

            System.Xml.XmlNamespaceManager nm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nm.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003");

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:Compile", nm))
                includes.Add(n.Attributes["Include"].Value);

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:Content", nm))
                content.Add(n.Attributes["Include"].Value);

            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:ItemGroup/ms:WixExtension", nm))
                refs.Add(n.Attributes["Include"].Value);


            string objdir = System.IO.Path.Combine(System.IO.Path.Combine(projdir, "obj"), config);
            string packagename = "output";
            string outdir = System.IO.Path.Combine("bin", config);
            string outtype = "Package";

            //TODO: Support multiconfiguration system correctly
            foreach (System.Xml.XmlNode n in doc.SelectNodes("ms:Project/ms:PropertyGroup/ms:Configuration", nm))
                if (true) //if (string.Compare(n.InnerText, config, true) == 0)
                {
                    System.Xml.XmlNode p = n.ParentNode;
                    if (p["OutputName"] != null)
                        packagename = p["OutputName"].InnerText;
                    if (p["OutputType"] != null)
                        outtype = p["OutputType"].InnerText;
                    if (p["OutputPath"] != null)
                        outdir = p["OutputPath"].InnerText.Replace("$(Configuration)", config);
                    if (p["IntermediateOutputPath"] != null)
                        objdir = p["IntermediateOutputPath"].InnerText.Replace("$(Configuration)", config);
                }

            if (!objdir.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                objdir += System.IO.Path.DirectorySeparatorChar;

            string msiname = System.IO.Path.Combine(outdir, packagename + ".msi");

            Console.WriteLine("  Compiling ... ");
            if (includes.Count == 0)
            {
                Console.WriteLine("No files found to compile in project file");
                return;
            }

            string compile_args = "\"" + string.Join("\" \"", includes.ToArray()) + "\"";
            compile_args += " -out \"" + objdir.Replace("\\", "\\\\") + "\"";

            if (options.ContainsKey("platform") && options["platform"] == "x64")
                compile_args += " -dPlatform=x64 -dWin64=yes";
            else
                compile_args += " -dPlatform=x86 -dWin64=no";

            int res = Execute(
                System.IO.Path.Combine(wixpath, "candle.exe"), 
                projdir,
                compile_args);

            if (res != 0)
            {
                Console.WriteLine("Compilation failed, aborting");
                return;
            }

            Console.WriteLine("  Linking ...");

            for (int i = 0; i < includes.Count; i++)
                includes[i] = System.IO.Path.Combine(objdir, System.IO.Path.GetFileNameWithoutExtension(includes[i]) + ".wixobj");

            for (int i = 0; i < refs.Count; i++)
                if (!System.IO.Path.IsPathRooted(refs[i]))
                {
                    refs[i] = FindDll(refs[i] + ".dll", new string[] { projdir, wixpath });
                    if (!System.IO.Path.IsPathRooted(refs[i]))
                        refs[i] = FindDll(refs[i]);
                }

            string link_args = "\"" + string.Join("\" \"", includes.ToArray()) + "\"";

            if (refs.Count > 0)
                link_args += " -ext \"" + string.Join("\" -ext \"", refs.ToArray()) + "\"";

            link_args += " -out \"" + msiname + "\"";

            res = Execute(
                System.IO.Path.Combine(wixpath, "light.exe"),
                projdir,
                link_args);

            if (res != 0)
            {
                Console.WriteLine("Link failed, aborting");
                return;
            }

            if (!System.IO.Path.IsPathRooted(msiname))
                msiname = System.IO.Path.GetFullPath(System.IO.Path.Combine(projdir, msiname));

            Console.WriteLine(string.Format("Done: {0}", msiname));
        }

        private static int Execute(string app, string workdir, string args)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(app, args);
            psi.CreateNoWindow = true;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = workdir;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
            p.WaitForExit(5 * 60 * 1000); //Wait up to five minutes
            if (!p.HasExited)
            {
                try { p.Kill(); }
                catch { }

                Console.WriteLine("Stdout: " + p.StandardOutput.ReadToEnd());
                Console.WriteLine("Stderr: " + p.StandardError.ReadToEnd());

                throw new Exception(string.Format("Application {0} hung", app));
            }

            Console.WriteLine();
            Console.WriteLine(p.StandardOutput.ReadToEnd());
            Console.WriteLine(p.StandardError.ReadToEnd());
            Console.WriteLine();


            return p.ExitCode;
        }

        private static string FindDll(string filename)
        {
            return FindDll(filename, System.Environment.GetEnvironmentVariable("path").Split(System.IO.Path.PathSeparator));
        }

        private static string FindDll(string filename, IEnumerable<string> paths)
        {
            foreach (string p in paths)
                if (System.IO.File.Exists(System.IO.Path.Combine(p, filename)))
                    return System.IO.Path.Combine(p, filename);

            return filename;
        }
    }
}
