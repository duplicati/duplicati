#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Backend
{

    class Program
    {
        const string HELPMESSAGE = 
            "Invalid number of arguments.\r\n\r\n" +
            "Usage: DuplicatiBackend <operation> <url> [options]\r\n\r\n" +
            "Operations:\r\n" +
            "\tHELP     \t: Return this message\r\n" +
            "\tBACKENDS \t: List the avalible backends\r\n" +
            "\tLIST     \t: Return a list of files at the given url\r\n" +
            "\tGET      \t: Write the file at the given url to stdout\r\n" +
            "\tPUT      \t: Write the contents of stdin to the given url\r\n" +
            "\tDELETE   \t: Delete the file at the given url\r\n" +
            "\r\n\r\n" +
            "Options:\r\n" +
            "\t--file=<filename> \t: For GET, use filename rather than stdin\r\n" +
            "                    \t: For PUT, use filename rather than stdout\r\n" +
            "                    \t: For LIST, use filename rather than stdout\r\n";

        static int Main(string[] args)
        {
            List<string> largs = new List<string>(args);

            Dictionary<string, string> options = ExtractOptions(largs);

            if (largs.Count < 1)
            {
                Console.WriteLine(HELPMESSAGE);
                return -1;
            }

            string operation = args[0].ToUpper();
            if (operation == "HELP")
            {
                Console.WriteLine(HELPMESSAGE);
                return 0;
            }
            else if (operation == "BACKENDS")
            {
                Console.WriteLine("Registered backends:\r\n");
                foreach (string b in BackendLoader.Backends)
                    Console.WriteLine(b + "\t: " + BackendLoader.GetBackend(b).DisplayName);

                return 0;
            }

            if (largs.Count != 2)
            {
                Console.WriteLine(HELPMESSAGE);
                return -1;
            }
            

            string url = largs[1];
            IBackendInterface backend = null;
            try
            {
                backend = new BackendLoader(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Failed to load backend: {0}", ex.Message));
                return -1;
            }

            string file = options.ContainsKey("file") ? options["file"] : null;

            System.IO.Stream s = null;

            try
            {
                switch (operation.Trim().ToUpper())
                {
                    case "LIST":
                        if (file == null)
                            s = Console.OpenStandardOutput();
                        else
                            s = new System.IO.FileStream(file, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);

                        using (System.IO.StreamWriter sw = new System.IO.StreamWriter(s))
                            foreach (FileEntry fe in backend.List(url, options))
                                sw.WriteLine(fe.Name);

                        break;
                    case "PUT":
                        if (file == null)
                            s = Console.OpenStandardInput();
                        else
                            s = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);

                        backend.Put(url, options, s);
                        break;
                    case "GET":
                        if (file == null)
                            s = Console.OpenStandardOutput();
                        else
                            s = new System.IO.FileStream(file, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
                        Utility.CopyStream(backend.Get(url, options), s, true);
                        break;
                    case "DELETE":
                        backend.Delete(url, options);
                        break;

                    default:
                        Console.WriteLine(HELPMESSAGE);
                        return -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Failed: {0}", ex.Message));
                return -1;
            }
            finally
            {
                if (file != null && s != null)
                {
                    try { s.Close(); }
                    catch { }

                    try { s.Dispose(); }
                    catch { }
                }

            }
            return 0;
        }

        private static Dictionary<string, string> ExtractOptions(List<string> args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>();

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string key = null;
                    string value = null;
                    if (args[i].IndexOf("=") > 0)
                    {
                        key = args[i].Substring(0, args[i].IndexOf("="));
                        value = args[i].Substring(args[i].IndexOf("=") + 1);
                    }
                    else
                        key = args[i];

                    //Skip the leading --
                    key = key.Substring(2);
                    if (!string.IsNullOrEmpty(value) && value.Length > 1 && value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);
                    options[key] = value;

                    i--;
                }
            }

            return options;

        }
    }
}
