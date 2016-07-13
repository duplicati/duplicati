using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class Recompress
    {
        public static int Run(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 4)
            {
                Console.WriteLine("Invalid argument count ({0} expected 4): {1}{2}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            string target_compr_module = args[1];

            if (!Library.DynamicLoader.CompressionLoader.Keys.Contains(target_compr_module))
            {
                Console.WriteLine("Target compression module not found: {0}{1}Modules supported: {2}", args[1], Environment.NewLine, string.Join(", ", Library.DynamicLoader.CompressionLoader.Keys));
                return 100;
            }

            var m_Options = new Options(options);

            using (var backend = Library.DynamicLoader.BackendLoader.GetBackend(args[2], options))
            {
                if (backend == null)
                {
                    Console.WriteLine("Backend not found: {0}{1}Backends supported: {2}", args[2], Environment.NewLine, string.Join(", ", Library.DynamicLoader.BackendLoader.Keys));
                    return 100;
                }

                var targetfolder = Path.GetFullPath(args[3]);

                if (!Directory.Exists(args[3]))
                {
                    Console.WriteLine("Creating target folder: {0}", targetfolder);
                    Directory.CreateDirectory(targetfolder);
                }

                Console.WriteLine("Listing files on backend: {0} ...", backend.ProtocolKey);

                var rawlist = backend.List();

                Console.WriteLine("Found {0} files", rawlist.Count);

                var i = 0;
                var downloaded = 0;
                var errors = 0;
                var needspass = 0;

                var remotefiles =
                    (from x in rawlist
                     let n = VolumeBase.ParseFilename(x)
                     where n != null && n.Prefix == m_Options.Prefix
                     select n).ToArray(); //ToArray() ensures that we do not remote-request it multiple times

                if (remotefiles.Length == 0)
                {
                    if (rawlist.Count == 0)
                        Console.WriteLine("No files were found at the remote location, perhaps the target url is incorrect?");
                    else
                    {
                        var tmp =
                            (from x in rawlist
                             let n = VolumeBase.ParseFilename(x)
                             where
                                 n != null
                             select n.Prefix).ToArray();

                        var types = tmp.Distinct().ToArray();
                        if (tmp.Length == 0)
                            Console.WriteLine("Found {0} files at the remote storage, but none that could be parsed", rawlist.Count);
                        else if (types.Length == 1)
                            Console.WriteLine("Found {0} parse-able files with the prefix {1}, did you forget to set the backup-prefix?", tmp.Length, types[0]);
                        else
                            Console.WriteLine("Found {0} parse-able files (of {1} files) with different prefixes: {2}, did you forget to set the backup-prefix?", tmp.Length, rawlist.Count, string.Join(", ", types));
                    }

                    return 100;
                }

                foreach (var entry in remotefiles)
                {
                    try
                    {
                        Console.Write("{0}: {1}", i, entry.File.Name);

                        var local = Path.Combine(targetfolder, entry.File.Name);
                        if (entry.EncryptionModule != null)
                        {
                            if (string.IsNullOrWhiteSpace(m_Options.Passphrase))
                            {
                                needspass++;
                                Console.WriteLine(" - No passphrase supplied, skipping");
                                continue;
                            }

                            local = local.Substring(0, local.Length - entry.EncryptionModule.Length - 1);
                        }

                        if (entry.CompressionModule == target_compr_module)
                        {
                            Console.WriteLine(" - compression types are same");
                            continue;
                        }

                        string localNew;

                        if (entry.CompressionModule != null)
                        {
                            localNew = local.Substring(0, local.Length - entry.CompressionModule.Length - 1) + "." + target_compr_module;

                            if (File.Exists(localNew))
                            {
                                Console.WriteLine(" - target file already exist");
                                continue;
                            }
                        }
                        else
                        {
                            Console.WriteLine(" - cannot detect compression type");
                            continue;
                        }

                        if (File.Exists(local))
                            File.Delete(local);

                        Console.Write(" - downloading ({0})...", Library.Utility.Utility.FormatSizeString(entry.File.Size));

                        using (var tf = new Library.Utility.TempFile())
                        {
                            backend.Get(entry.File.Name, tf);
                            downloaded++;

                            if (entry.EncryptionModule != null)
                            {
                                Console.Write(" decrypting ...");
                                using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(entry.EncryptionModule, m_Options.Passphrase, options))
                                using (var tf2 = new Library.Utility.TempFile())
                                {
                                    m.Decrypt(tf, tf2);
                                    File.Copy(tf2, local);
                                    File.Delete(tf2);
                                }
                            }
                            else
                                File.Copy(tf, local);

                            File.Delete(tf);
                        }

                        if (entry.CompressionModule != null)
                        {
                            Console.Write(" recompressing ...");

                            using (var cmOld = Library.DynamicLoader.CompressionLoader.GetModule(entry.CompressionModule, local, options))
                            {
                                using (var cmNew = Library.DynamicLoader.CompressionLoader.GetModule(target_compr_module, localNew, options))
                                {
                                    foreach (var cmfile in cmOld.ListFiles(""))
                                        using (var sourceStream = cmOld.OpenRead(cmfile))
                                        using (var cs = cmNew.CreateFile(cmfile, Duplicati.Library.Interface.CompressionHint.Compressible, cmOld.GetLastWriteTime(cmfile)))
                                            Library.Utility.Utility.CopyStream(sourceStream, cs);
                                }
                            }

                            File.Delete(local);
                        }

                        bool reencrypt = Library.Utility.Utility.ParseBoolOption(options, "reencrypt");

                        if (reencrypt && entry.EncryptionModule != null)
                        {
                            Console.Write(" reencrypting ...");
                            using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(entry.EncryptionModule, m_Options.Passphrase, options))
                            {
                                m.Encrypt(localNew, localNew + "." + m.FilenameExtension);
                                File.Delete(localNew);
                                localNew = localNew + "." + m.FilenameExtension;
                            }
                        }

                        bool reupload = Library.Utility.Utility.ParseBoolOption(options, "reupload");

                        if (reupload)
                        {
                            backend.Put((new FileInfo(localNew)).Name, localNew);
                            backend.Delete(entry.File.Name);
                            File.Delete(localNew);
                        }

                        Console.WriteLine(" done!");

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(" error: {0}", ex.ToString());
                        errors++;
                    }

                    i++;
                }

                if (needspass > 0 && downloaded == 0)
                {
                    Console.WriteLine("No files downloaded, try adding --passphrase to decrypt files");
                    return 100;
                }

                Console.WriteLine("Download complete, of {0} remote files, {1} were downloaded with {2} errors", remotefiles.Count(), downloaded, errors);
                if (needspass > 0)
                    Console.WriteLine("Additonally {0} remote files were skipped because of encryption, supply --passphrase to download those");

                if (errors > 0)
                {
                    Console.WriteLine("There were errors during recompress of remote backend files!");
                    return 200;
                }

                return 0;
            }
        }
    }
}

