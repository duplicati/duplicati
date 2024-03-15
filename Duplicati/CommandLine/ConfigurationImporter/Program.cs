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

using Duplicati.Server.Serializable;
using Duplicati.Server.WebServer.RESTMethods;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.CommandLine.ConfigurationImporter
{
    public static class ConfigurationImporter
    {
        private static readonly string usageString = $"Usage: {nameof(ConfigurationImporter)}.exe <configuration-file> --import-metadata=(true | false) --server-datafolder=<folder containing Duplicati-server.sqlite>";

        public static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                throw new ArgumentException($"Incorrect number of input arguments.  {ConfigurationImporter.usageString}");
            }

            string configurationFile = args[0];
            Dictionary<string, string> importOptions = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args.Skip(1).ToList());
            if (!importOptions.TryGetValue("import-metadata", out string importMetadataString))
            {
                throw new ArgumentException($"Invalid import-metadata argument.  {ConfigurationImporter.usageString}");
            }
            bool importMetadata = Duplicati.Library.Utility.Utility.ParseBool(importMetadataString, false);

            if (!importOptions.TryGetValue("server-datafolder", out string serverDatafolder))
            {
                throw new ArgumentException($"Invalid server-datafolder argument.  {ConfigurationImporter.usageString}");
            }

            Dictionary<string, string> advancedOptions = new Dictionary<string, string>
            {
                { "server-datafolder", serverDatafolder }
            };

            ImportExportStructure importedStructure = Backups.ImportBackup(configurationFile, importMetadata, () => ConfigurationImporter.ReadPassword($"Password for {configurationFile}: "), advancedOptions);
            Console.WriteLine($"Imported \"{importedStructure.Backup.Name}\" with ID {importedStructure.Backup.ID} and local database at {importedStructure.Backup.DBPath}.");

            return 0;
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            string password = "";
            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    break;
                }
                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password = password.Substring(0, password.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else if (keyInfo.KeyChar != '\u0000') // Only accept if the key maps to a Unicode character (e.g., ignore F1 or Home).
                {

                    password += keyInfo.KeyChar;
                    Console.Write("*");
                }
            }

            Console.WriteLine();
            return password;
        }
    }
}
