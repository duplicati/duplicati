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
using System.Linq;

namespace LocalizationTool
{
    public static class ResXCompiler
    {
        public static void CompileResxFiles(string folder, List<string> excludeFolders, string @namespace, string assemblyname, string versionAssembly, string keyfile, string culture, string productname)
        {
            folder = Duplicati.Library.Utility.Utility.AppendDirSeparator(folder);
            string resgenexe = System.Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\Microsoft SDKs\\Windows\\v6.0A\\bin\\resgen.exe");
            string alexe = System.Environment.ExpandEnvironmentVariables("%WINDIR%\\Microsoft.Net\\Framework\\v2.0.50727\\al.exe");

            if (!System.IO.File.Exists(resgenexe))
            {
                string resgenexe2 = System.Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\Microsoft.NET\\SDK\\v2.0\\bin\\resgen.exe");

                if (System.IO.File.Exists(resgenexe2))
                    resgenexe = resgenexe2;
                else
                {
                    Console.WriteLine("Unable to locate file: {0}", resgenexe);
                    Console.WriteLine("This can be fixed by installing a microsoft platform SDK, or visual studio (express is fine)");
                    return;
                }
            }
            if (!System.IO.File.Exists(alexe))
            {
                string v30 = System.Environment.ExpandEnvironmentVariables("%WINDIR%\\Microsoft.Net\\Framework\\v3.0\\al.exe");
                string v35 = System.Environment.ExpandEnvironmentVariables("%WINDIR%\\Microsoft.Net\\Framework\\v3.5\\al.exe");
                string sdk = System.Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\Microsoft SDKs\\Windows\\v6.0A\\bin\\al.exe");

                if (System.IO.File.Exists(v30))
                    alexe = v30;
                else if (System.IO.File.Exists(v35))
                    alexe = v35;
                else if (System.IO.File.Exists(sdk))
                    alexe = sdk;
            }

            if (!System.IO.File.Exists(alexe))
            {
                Console.WriteLine("Unable to locate file: {0}", alexe);
                Console.WriteLine("This can be fixed by installing the .Net framework version 2.0");
                return;
            }

            List<string> resources = new List<string>();

            foreach (string s in Duplicati.Library.Utility.Utility.EnumerateFiles(folder))
            {
                if (s.ToLower().EndsWith("." + culture.ToLower() + ".resx"))
                {
                    if (excludeFolders.Any(xf => s.ToLower().StartsWith(Duplicati.Library.Utility.Utility.AppendDirSeparator(xf).ToLower())))
                        continue;

                    string resname = System.IO.Path.ChangeExtension(s, ".resources");

                    if (!System.IO.File.Exists(resname) || System.IO.File.GetLastWriteTime(resname) < System.IO.File.GetLastWriteTime(s))
                    {
                        Console.WriteLine("Compiling: " + s);
                        System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(resgenexe, "\"" + s + "\"");
                        pi.CreateNoWindow = true;
                        pi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                        pi.RedirectStandardOutput = true;
                        pi.RedirectStandardError = true;
                        pi.UseShellExecute = false;
                        pi.WorkingDirectory = System.IO.Path.GetDirectoryName(s);

                        System.Diagnostics.Process pr = System.Diagnostics.Process.Start(pi);
                        pr.WaitForExit();

                        if (pr.ExitCode != 0)
                        {
                            Console.WriteLine("Error");
                            Console.WriteLine(pr.StandardOutput.ReadToEnd());
                            Console.WriteLine(pr.StandardError.ReadToEnd());
                            throw new Exception("Resgen failure: " + s);
                        }
                    }
                    else
                        Console.WriteLine("Not modified: " + s);

                    resources.Add(resname);
                }
            }

            if (resources.Count == 0)
                return;

            if (!System.IO.File.Exists(versionAssembly))
            {
                Console.WriteLine("Unable to locate file: {0}", versionAssembly);
                Console.WriteLine("This can be fixed by compiling the application or modifying the file configuration.xml");
                return;
            }

            using (Duplicati.Library.Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
            {
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tf))
                {
                    System.Reflection.Assembly asm = System.Reflection.Assembly.ReflectionOnlyLoadFrom(versionAssembly);

                    sw.WriteLine("/t:lib");
                    sw.WriteLine("/out:\"" + assemblyname + "\"");
                    sw.WriteLine("/product:\"" + productname + "\"");
                    sw.WriteLine("/title:\"" + productname + "\"");
                    sw.WriteLine("/version:" + asm.GetName().Version.ToString());
                    if (!string.IsNullOrEmpty(keyfile))
                        sw.WriteLine("/keyfile:\"" + keyfile + "\"");
                    sw.WriteLine("/culture:" + culture);

                    foreach (string s in resources)
                    {
                        string resname = s.Substring(folder.Length);
                        resname = resname.Replace("\\", ".");
                        resname = resname.Replace(" ", "_");
                        resname = @namespace + "." + resname;

                        sw.WriteLine("/embed:\"" + s + "\"," + resname);
                    }
                }

                Console.WriteLine("Linking ...");
                System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(alexe, "@\"" + tf + "\"");
                pi.CreateNoWindow = true;
                pi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

                System.Diagnostics.Process pr = System.Diagnostics.Process.Start(pi);
                pr.WaitForExit();

                if (pr.ExitCode != 0)
                    throw new Exception("Linker failure");
            }

        }
    }
}
