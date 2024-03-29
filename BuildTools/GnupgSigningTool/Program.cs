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
using System.Collections.Generic;

namespace GnupgSigningTool
{
    public static class Program
    {

        private static string keyfilepassword;

        private static string gpgkeypassphrase;
        private static string gpgkeyfile;
        private static string gpgpath;
        private static string gpgkeyid;
        private static bool useArmor;

        private static string inputFile;
        private static string signatureFile;

        private static void SpawnGPG()
        {

            var armorOption = useArmor ? "--armor" : "";

            var gpgArgument = string.Format("--pinentry-mode loopback --passphrase-fd 0 --batch --yes {0} -u \"{1}\" --output \"{2}\" --detach-sig \"{3}\"",
                                            armorOption,
                                            gpgkeyid,
                                            signatureFile,
                                            inputFile);

            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = gpgpath,
                Arguments = gpgArgument,
                RedirectStandardInput = true,
                UseShellExecute = false
            });

            proc.StandardInput.WriteLine(gpgkeypassphrase);

            proc.WaitForExit();
        }

        private static void LoadGPGKeyIdAndPassphrase()
        {
            using (var enc = new Duplicati.Library.Encryption.AESEncryption(keyfilepassword, new Dictionary<string, string>()))
            using (var ms = new System.IO.MemoryStream())
            using (var fs = System.IO.File.OpenRead(gpgkeyfile))
            {
                try
                {
                    enc.Decrypt(fs, ms);
                }
                catch (System.Security.Cryptography.CryptographicException e)
                {
                    throw new ArgumentException("Failed to decrypt gpg secret credentials file: {0}\n", e.Message);
                }

                ms.Position = 0;

                using (var sr = new System.IO.StreamReader(ms))
                {
                    var lines = sr.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    gpgkeyid = lines[0];
                    gpgkeypassphrase = lines[1];
                }
            }
        }


        public static void Main(string[] _args)
        {
            var args = new List<string>(_args);
            var opts = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);

            opts.TryGetValue("inputfile", out inputFile);
            opts.TryGetValue("signaturefile", out signatureFile);
            opts.TryGetValue("keyfile-password", out keyfilepassword);
            opts.TryGetValue("gpgkeyfile", out gpgkeyfile);
            opts.TryGetValue("gpgpath", out gpgpath);
            opts.TryGetValue("armor", out string armor);

            useArmor = Boolean.TryParse(armor, out useArmor) && useArmor;

            if (string.IsNullOrWhiteSpace(gpgkeyfile))
            {
                throw new ArgumentException("No gpgfile with encrypted credentials specified.");
            }

            if (!System.IO.File.Exists(gpgkeyfile))
            {
                throw new ArgumentException("Specified file with encrypted gpg credentials not found.");
            }

            LoadGPGKeyIdAndPassphrase();

            if (gpgkeyid is null || gpgkeypassphrase is null)
            {
                throw new ArgumentException("Could not fetch gpg key id or gpg passphrase.");
            }

            gpgpath = gpgpath ?? Duplicati.Library.Encryption.GPGEncryption.GetGpgProgramPath();

            SpawnGPG();
        }
    }
}
