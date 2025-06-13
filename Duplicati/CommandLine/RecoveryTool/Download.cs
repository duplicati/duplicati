// Copyright (C) 2025, The Duplicati Team
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;
using System.Threading.Tasks;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class Download
    {
        public static int Run(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 3)
            {
                Console.WriteLine("Invalid argument count ({0} expected 3): {1}{2}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            using (var backend = Library.DynamicLoader.BackendLoader.GetBackend(args[1], options))
            {
                if (backend == null)
                {
                    Console.WriteLine("Backend not found: {0}{1}Backends supported: {2}", args[1], Environment.NewLine, string.Join(", ", Library.DynamicLoader.BackendLoader.Keys));
                    return 100;
                }

                var targetfolder = Path.GetFullPath(args[2]);

                if (!Directory.Exists(args[2]))
                {
                    Console.WriteLine("Creating target folder: {0}", targetfolder);
                    Directory.CreateDirectory(targetfolder);
                }

                Console.WriteLine("Listing files on backend: {0} ...", backend.ProtocolKey);

                var lst = backend.ListAsync(CancellationToken.None).ToBlockingEnumerable().ToList();

                Console.WriteLine("Found {0} files", lst.Count);

                var i = 0;
                var downloaded = 0;
                var errors = 0;
                var needspass = 0;
                string passphrase;
                options.TryGetValue("passphrase", out passphrase);

                foreach (var file in lst)
                {
                    try
                    {
                        Console.Write("{0}: {1}", i, file.Name);
                        var p = Duplicati.Library.Main.Volumes.VolumeBase.ParseFilename(file);
                        if (p == null)
                        {
                            Console.WriteLine(" - Not a Duplicati file, ignoring");
                            continue;
                        }

                        var local = Path.Combine(targetfolder, file.Name);
                        if (p.EncryptionModule != null)
                        {
                            if (string.IsNullOrWhiteSpace(passphrase))
                            {
                                needspass++;
                                Console.WriteLine(" - No passphrase supplied, skipping");
                                continue;
                            }

                            local = local.Substring(0, local.Length - p.EncryptionModule.Length - 1);
                        }

                        if (p.FileType != Duplicati.Library.Main.RemoteVolumeType.Blocks && p.FileType != Duplicati.Library.Main.RemoteVolumeType.Files)
                        {
                            Console.WriteLine(" - Filetype {0}, skipping", p.FileType);
                            continue;
                        }

                        if (File.Exists(local))
                        {
                            Console.WriteLine(" - Already exists, skipping");
                            continue;
                        }

                        Console.Write(" - downloading ({0})...", Library.Utility.Utility.FormatSizeString(file.Size));

                        using (var tf = new Library.Utility.TempFile())
                        {
                            backend.GetAsync(file.Name, tf, CancellationToken.None).Await();

                            if (p.EncryptionModule != null)
                            {
                                Console.Write(" - decrypting ...");
                                using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(p.EncryptionModule, passphrase, options))
                                using (var tf2 = new Library.Utility.TempFile())
                                {
                                    m.Decrypt(tf, tf2);
                                    File.Copy(tf2, local);
                                }
                            }
                            else
                                File.Copy(tf, local);
                        }

                        Console.WriteLine(" done!");

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(" error: {0}", ex);
                        errors++;
                    }

                    i++;
                }

                if (needspass > 0 && downloaded == 0)
                {
                    Console.WriteLine("No files downloaded, try adding --passphrase to decrypt files");
                    return 100;
                }

                Console.WriteLine("Download complete, of {0} remote files, {1} were downloaded with {2} errors", lst.Count, downloaded, errors);
                if (needspass > 0)
                    Console.WriteLine("Additonally {0} remote files were skipped because of encryption, supply --passphrase to download those", needspass);

                if (errors > 0)
                    return 200;
                else
                    return 0;

            }
        }
    }
}

