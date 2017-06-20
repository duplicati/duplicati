using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using Newtonsoft.Json.Linq;

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

                Console.WriteLine("Found {0} files at remote storage", rawlist.Count);

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
                            Console.WriteLine("Found {0} parse-able files with the prefix {1}, did you forget to set the backup prefix?", tmp.Length, types[0]);
                        else
                            Console.WriteLine("Found {0} parse-able files (of {1} files) with different prefixes: {2}, did you forget to set the backup prefix?", tmp.Length, rawlist.Count, string.Join(", ", types));
                    }

                    return 100;
                }

                bool reencrypt = Library.Utility.Utility.ParseBoolOption(options, "reencrypt");
                bool reupload = Library.Utility.Utility.ParseBoolOption(options, "reupload");

                // Needs order (Files or Blocks) and Indexes as last because indexes content will be adjusted based on recompressed blocks
                var files = remotefiles.Where(a => a.FileType == RemoteVolumeType.Files).ToArray();
                var blocks = remotefiles.Where(a => a.FileType == RemoteVolumeType.Blocks).ToArray();
                var indexes = remotefiles.Where(a => a.FileType == RemoteVolumeType.Index).ToArray();

                remotefiles = files.Concat(blocks).ToArray().Concat(indexes).ToArray();

                Console.WriteLine("Found {0} files which belongs to backup with prefix {1}", remotefiles.Count(), m_Options.Prefix);

                foreach (var remoteFile in remotefiles)
                {
                    try
                    {
                        Console.Write("{0}/{1}: {2}", ++i, remotefiles.Count(), remoteFile.File.Name);

                        var localFileSource = Path.Combine(targetfolder, remoteFile.File.Name);
                        string localFileTarget;
                        string localFileSourceEncryption = "";

                        if (remoteFile.EncryptionModule != null)
                        {
                            if (string.IsNullOrWhiteSpace(m_Options.Passphrase))
                            {
                                needspass++;
                                Console.WriteLine(" - No passphrase supplied, skipping");
                                continue;
                            }

                            using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(remoteFile.EncryptionModule, m_Options.Passphrase, options))
                                localFileSourceEncryption = m.FilenameExtension;

                            localFileSource = localFileSource.Substring(0, localFileSource.Length - localFileSourceEncryption.Length - 1);
                        }

                        if (remoteFile.CompressionModule != null)
                            localFileTarget = localFileSource.Substring(0, localFileSource.Length - remoteFile.CompressionModule.Length - 1) + "." + target_compr_module;
                        else
                        {
                            Console.WriteLine(" - cannot detect compression type");
                            continue;
                        }                        

                        if ((!reencrypt && File.Exists(localFileTarget)) || (reencrypt && File.Exists(localFileTarget + "." + localFileSourceEncryption)))
                        {
                            Console.WriteLine(" - target file already exist");
                            continue;
                        }

                        if (File.Exists(localFileSource))
                            File.Delete(localFileSource);

                        Console.Write(" - downloading ({0})...", Library.Utility.Utility.FormatSizeString(remoteFile.File.Size));

                        DateTime originLastWriteTime;
                        FileInfo destinationFileInfo;

                        using (var tf = new TempFile())
                        {
                            backend.Get(remoteFile.File.Name, tf);
                            originLastWriteTime = new FileInfo(tf).LastWriteTime;
                            downloaded++;

                            if (remoteFile.EncryptionModule != null)
                            {
                                Console.Write(" decrypting ...");
                                using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(remoteFile.EncryptionModule, m_Options.Passphrase, options))
                                using (var tf2 = new TempFile())
                                {
                                    m.Decrypt(tf, tf2);
                                    File.Copy(tf2, localFileSource);
                                    File.Delete(tf2);
                                }
                            }
                            else
                                File.Copy(tf, localFileSource);

                            File.Delete(tf);
                            destinationFileInfo = new FileInfo(localFileSource);
                            destinationFileInfo.LastWriteTime = originLastWriteTime;
                        }

                        if (remoteFile.CompressionModule != null)
                        {
                            Console.Write(" recompressing ...");

                            //Recompressing from eg. zip to zip
                            if (localFileSource == localFileTarget)
                            {
                                File.Move(localFileSource, localFileSource + ".same");
                                localFileSource = localFileSource + ".same";
                            }

                            using (var cmOld = Library.DynamicLoader.CompressionLoader.GetModule(remoteFile.CompressionModule, localFileSource, options))
                            using (var cmNew = Library.DynamicLoader.CompressionLoader.GetModule(target_compr_module, localFileTarget, options))
                                foreach (var cmfile in cmOld.ListFiles(""))
                                {
                                    string cmfileNew = cmfile;
                                    var cmFileVolume = VolumeBase.ParseFilename(cmfileNew);
                                                                            
                                    if (remoteFile.FileType == RemoteVolumeType.Index && cmFileVolume != null && cmFileVolume.FileType == RemoteVolumeType.Blocks)
                                    {
                                        // Correct inner filename extension to target compression type
                                        cmfileNew = cmfileNew.Replace("." + cmFileVolume.CompressionModule, "." + target_compr_module);
                                        if (!reencrypt)
                                            cmfileNew = cmfileNew.Replace("." + cmFileVolume.EncryptionModule, "");

                                        //Because compression changes blocks file sizes - needs to be updated
                                        string textJSON;
                                        using (var sourceStream = cmOld.OpenRead(cmfile))
                                        using (var sourceStreamReader = new StreamReader(sourceStream))
                                        {
                                            textJSON = sourceStreamReader.ReadToEnd();
                                            JToken token = JObject.Parse(textJSON);
                                            var fileInfoBlocks = new FileInfo(Path.Combine(targetfolder, cmfileNew.Replace("vol/", "")));
                                            var filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_Options.FileHashAlgorithm);
                                            
                                            using (var fileStream = fileInfoBlocks.Open(FileMode.Open))
                                            {
                                                fileStream.Position = 0;
                                                token["volumehash"] = Convert.ToBase64String(filehasher.ComputeHash(fileStream));
                                                fileStream.Close();
                                            }

                                            token["volumesize"] = fileInfoBlocks.Length;
                                            textJSON = token.ToString();
                                        }
                                            
                                        using (var sourceStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(textJSON)))
                                        using (var cs = cmNew.CreateFile(cmfileNew, Library.Interface.CompressionHint.Compressible, cmOld.GetLastWriteTime(cmfile)))
                                            Library.Utility.Utility.CopyStream(sourceStream, cs);
                                    }
                                    else
                                    {
                                        using (var sourceStream = cmOld.OpenRead(cmfile))
                                        using (var cs = cmNew.CreateFile(cmfileNew, Library.Interface.CompressionHint.Compressible, cmOld.GetLastWriteTime(cmfile)))
                                            Library.Utility.Utility.CopyStream(sourceStream, cs);
                                    }
                                }
                              
                            File.Delete(localFileSource);
                            destinationFileInfo = new FileInfo(localFileTarget);
                            destinationFileInfo.LastWriteTime = originLastWriteTime;
                        }
                        
                        if (reencrypt && remoteFile.EncryptionModule != null)
                        {
                            Console.Write(" reencrypting ...");
                            using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(remoteFile.EncryptionModule, m_Options.Passphrase, options))
                            {
                                m.Encrypt(localFileTarget, localFileTarget + "." + localFileSourceEncryption);
                                File.Delete(localFileTarget);
                                localFileTarget = localFileTarget + "." + localFileSourceEncryption;
                            }

                            destinationFileInfo = new FileInfo(localFileTarget);
                            destinationFileInfo.LastWriteTime = originLastWriteTime;
                        }
                        
                        if (reupload)
                        {
                            Console.Write(" reuploading ...");
                            backend.Put((new FileInfo(localFileTarget)).Name, localFileTarget);
                            backend.Delete(remoteFile.File.Name);
                            File.Delete(localFileTarget);
                        }

                        Console.WriteLine(" done!");

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(" error: {0}", ex.ToString());
                        errors++;
                    }
                }

                if (reupload)
                {
                    var remoteverificationfileexist = rawlist.Any(x => x.Name == (m_Options.Prefix + "-verification.json"));

                    if (remoteverificationfileexist)
                    {
                        Console.WriteLine("Found verification file {0} - deleting", m_Options.Prefix + "-verification.json");
                        backend.Delete(m_Options.Prefix + "-verification.json");
                    }
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

