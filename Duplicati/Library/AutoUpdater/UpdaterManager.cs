//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.AutoUpdater
{
    public class UpdaterManager
    {
        private System.Security.Cryptography.RSACryptoServiceProvider m_key;
        private string[] m_urls;
        private string m_appname;
        private string m_installdir;

        public event Action<Exception> OnError;

        private const string DATETIME_FORMAT = "yyyymmddhhMMss";
        private const string INSTALLDIR_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_INSTALL_ROOT";
        private const string RUN_UPDATED_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_LOAD_UPDATE";
        private const string UPDATE_MANIFEST_FILENAME = "autoupdate.manifest";

        /// <summary>
        /// Gets the original directory that this application was installed into
        /// </summary>
        /// <value>The original directory that this application was installed into</value>
        public string InstalledBaseDir
        {
            get
            {
                var s = System.Environment.GetEnvironmentVariable(string.Format(INSTALLDIR_ENVNAME_TEMPLATE, m_appname));
                if (string.IsNullOrWhiteSpace(s))
                    return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                else
                    return s;
            }
        }

        public UpdaterManager(string[] urls, System.Security.Cryptography.RSACryptoServiceProvider key, string appname, string installdir = null)
        {
            m_key = key;
            m_urls = urls;
            m_appname = appname;
            m_installdir = installdir;
            if (string.IsNullOrWhiteSpace(m_installdir))
            {
                var attempts = new string[] {
                    System.IO.Path.Combine(InstalledBaseDir, "updates"),
                    System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), m_appname, "updates"),
                    System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), m_appname, "updates"),
                    System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), m_appname, "updates"),
                };

                foreach(var p in attempts)
                    if (TestDirectoryIsWriteable(p))
                    {
                        m_installdir = p;
                        break;
                    }
            }
        }

        private bool TestDirectoryIsWriteable(string path)
        {
            var p2 = System.IO.Path.Combine(path, "test-" + DateTime.UtcNow.ToString(DATETIME_FORMAT));
            var probe = System.IO.Directory.Exists(path) ? p2 : path;

            if (!System.IO.Directory.Exists(probe))
            {
                try 
                {
                    System.IO.Directory.CreateDirectory(probe);
                    if (probe != path)
                        System.IO.Directory.Delete(probe);       
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        public IEnumerable<UpdateInfo> CheckForUpdate()
        {
            foreach(var url in m_urls)
            {
                try
                {
                    using(var tmpfile = new Library.Utility.TempFile())
                    {
                        System.Net.WebClient wc = new System.Net.WebClient();
                        wc.DownloadFile(url, tmpfile);

                        using(var fs = System.IO.File.OpenRead(tmpfile))
                        using(var ss = new SignatureReadingStream(fs, m_key))
                        using(var tr = new System.IO.StreamReader(ss))
                        using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                            return new Newtonsoft.Json.JsonSerializer().Deserialize<IEnumerable<UpdateInfo>>(jr);
                    }
                }
                catch (Exception ex)
                {
                    if (OnError != null)
                        OnError(ex);
                }
            }

            return new UpdateInfo[0];
        }

        public IEnumerable<KeyValuePair<string, UpdateInfo>> FindInstalledVersions()
        {
            var res = new List<KeyValuePair<string, UpdateInfo>>();
            foreach(var folder in System.IO.Directory.GetDirectories(m_installdir))
            {
                var manifest = System.IO.Path.Combine(folder, UPDATE_MANIFEST_FILENAME);
                if (System.IO.File.Exists(manifest))
                {
                    try
                    {
                        using(var fs = System.IO.File.OpenRead(manifest))
                        using(var ss = new SignatureReadingStream(fs, m_key))
                        using(var tr = new System.IO.StreamReader(ss))
                        using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                            res.Add(new KeyValuePair<string, UpdateInfo>(folder, new Newtonsoft.Json.JsonSerializer().Deserialize<UpdateInfo>(jr)));
                    }
                    catch (Exception ex)
                    {
                        if (OnError != null)
                            OnError(ex);
                    }
                }
            }

            return res;
        }

        public bool DownloadAndUnpackUpdate(UpdateInfo version)
        {
            using(var tempfile = new Library.Utility.TempFile())
            {
                foreach(var url in version.RemoteURLS)
                {
                    try
                    {
                        System.Net.WebClient wc = new System.Net.WebClient();
                        wc.DownloadFile(url, tempfile);

                        var sha256 = System.Security.Cryptography.SHA256.Create();
                        var md5 =  System.Security.Cryptography.MD5.Create();

                        using(var s = System.IO.File.OpenRead(tempfile))
                            if (s.Length != version.CompressedSize)
                                throw new Exception(string.Format("Invalid file size {0}, expected {1} for {2}", s.Length, version.CompressedSize, url));
                            else if (Convert.ToBase64String(sha256.ComputeHash(s)) != version.SHA256)
                                throw new Exception(string.Format("Damaged or corrupted file, sha256 mismatch for {0}", url));

                        using(var s = System.IO.File.OpenRead(tempfile))
                            if (Convert.ToBase64String(md5.ComputeHash(s)) != version.MD5)
                                throw new Exception(string.Format("Damaged or corrupted file, md5 mismatch for {0}", url));
                        
                        using(var tempfolder = new Duplicati.Library.Utility.TempFolder())
                        using(var zip = new Duplicati.Library.Compression.FileArchiveZip(tempfile, new Dictionary<string, string>()))
                        {
                            foreach(var file in zip.ListFilesWithSize(""))
                            {
                                if (System.IO.Path.IsPathRooted(file.Key) || file.Key.Trim().StartsWith("..", StringComparison.InvariantCultureIgnoreCase))
                                    throw new Exception(string.Format("Out-of-place file path detected: {0}", file.Key));

                                var targetpath = System.IO.Path.Combine(tempfolder, file.Key);
                                var targetfolder = System.IO.Path.GetDirectoryName(targetpath);
                                if (!System.IO.Directory.Exists(targetfolder))
                                    System.IO.Directory.CreateDirectory(targetfolder);

                                using(var zs = zip.OpenRead(file.Key))
                                using(var fs = System.IO.File.Create(targetpath))
                                    zs.CopyTo(fs);
                            }

                            if (VerifyUnpackedFolder(tempfolder, version))
                            {
                                var targetfolder = System.IO.Path.Combine(m_installdir, m_appname, version.ReleaseTime.ToString(DATETIME_FORMAT));
                                System.IO.Directory.Move(tempfolder, targetfolder);
                                return true;
                            }
                            else
                            {
                                throw new Exception(string.Format("Unable to verify unpacked folder for url: {0}", url));
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (OnError != null)
                            OnError(ex);
                    }
                }
            }

            return false;
        }

        public bool VerifyUnpackedFolder(string folder, UpdateInfo version = null)
        {
            try
            {
                UpdateInfo update;
                FileEntry manifest;

                var sha256 = System.Security.Cryptography.SHA256.Create();
                var md5 = System.Security.Cryptography.MD5.Create();

                using(var fs = System.IO.File.OpenRead(System.IO.Path.Combine(folder, UPDATE_MANIFEST_FILENAME)))
                {
                    using(var ss = new SignatureReadingStream(fs, m_key))
                    using(var tr = new System.IO.StreamReader(ss))
                    using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                        update = new Newtonsoft.Json.JsonSerializer().Deserialize<UpdateInfo>(jr);

                    sha256.Initialize();
                    md5.Initialize();

                    fs.Position = 0;
                    var h1 = Convert.ToBase64String(sha256.ComputeHash(fs));
                    fs.Position = 0;
                    var h2 = Convert.ToBase64String(md5.ComputeHash(fs));

                    manifest = new FileEntry() {
                        Path = UPDATE_MANIFEST_FILENAME,
                        Ignore = false,
                        LastWriteTime = update.ReleaseTime,
                        SHA256 = h1,
                        MD5 = h2
                    };
                }

                if (version != null && (update.Displayname != version.Displayname || update.ReleaseTime != version.ReleaseTime))
                    throw new Exception("The found version was not the expected version");

                var paths = update.Files.Where(x => !x.Ignore).ToDictionary(x => x.Path.Replace('/', System.IO.Path.DirectorySeparatorChar), Library.Utility.Utility.ClientFilenameStringComparer);
                paths.Add(manifest.Path, manifest);

                var ignores = (from x in update.Files where x.Ignore select Library.Utility.Utility.AppendDirSeparator(x.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))).ToList();

                folder = Library.Utility.Utility.AppendDirSeparator(folder);
                var baselen = folder.Length;

                foreach(var file in Library.Utility.Utility.EnumerateFileSystemEntries(folder))
                {
                    var relpath = file.Substring(baselen);
                    FileEntry fe;
                    if (!paths.TryGetValue(relpath, out fe))
                    {
                        var ignore = false;
                        foreach(var c in ignores)
                            if (ignore = relpath.StartsWith(c))
                                break;

                        if (ignore)
                            continue;

                        throw new Exception(string.Format("Found unexpected file: {0}", file));
                    }

                    paths.Remove(relpath);

                    if (fe.Path.EndsWith("/"))
                        continue;

                    sha256.Initialize();
                    md5.Initialize();

                    using(var fs = System.IO.File.OpenRead(file))
                    {
                        if (Convert.ToBase64String(sha256.ComputeHash(fs)) != fe.SHA256)
                            throw new Exception(string.Format("Invalid sha256 hash for file: {0}", file));

                        fs.Position = 0;
                        if (Convert.ToBase64String(md5.ComputeHash(fs)) != fe.MD5)
                            throw new Exception(string.Format("Invalid md5 hash for file: {0}", file));
                    }
                }

                if (paths.Count == 1)
                    throw new Exception(string.Format("Folder {0} is missing: {1}", folder, paths.First().Key));
                else if (paths.Count > 0)
                    throw new Exception(string.Format("Folder {0} is missing {1} files", folder, paths.Count));

                return true;
            }
            catch (Exception ex)
            {
                if (OnError != null)
                    OnError(ex);
            }

            return false;
        }

        public void CreateUpdatePackage(System.Security.Cryptography.RSACryptoServiceProvider key, string inputfolder, string outputfolder, string manifest = null)
        {
            // Read the existing manifest

            UpdateInfo remoteManifest;

            var manifestpath = manifest ?? System.IO.Path.Combine(inputfolder, UPDATE_MANIFEST_FILENAME);

            using(var s = System.IO.File.OpenRead(manifestpath))
            using(var sr = new System.IO.StreamReader(s))
            using(var jr = new Newtonsoft.Json.JsonTextReader(sr))
                remoteManifest = new Newtonsoft.Json.JsonSerializer().Deserialize<UpdateInfo>(jr);
            
            if (remoteManifest.Files == null)
                remoteManifest.Files = new FileEntry[0];

            if (remoteManifest.ReleaseTime.Ticks == 0)
                remoteManifest.ReleaseTime = DateTime.UtcNow;

            var ignoreFiles = (from n in remoteManifest.Files
                                        where n.Ignore
                                        select n).ToArray();

            var ignoreMap = ignoreFiles.ToDictionary(k => k.Path, k => "", Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);

            remoteManifest.MD5 = null;
            remoteManifest.SHA256 = null;
            remoteManifest.Files = null;
            remoteManifest.UncompressedSize = 0;

            var localManifest = remoteManifest.Clone();
            localManifest.RemoteURLS = null;

            inputfolder = Duplicati.Library.Utility.Utility.AppendDirSeparator(inputfolder);
            var baselen = inputfolder.Length;
            var dirsep = System.IO.Path.DirectorySeparatorChar.ToString();

            ignoreMap.Add(UPDATE_MANIFEST_FILENAME, "");

            var md5 = System.Security.Cryptography.MD5.Create();
            var sha256 = System.Security.Cryptography.SHA256.Create();

            Func<string, string> computeMD5 = (path) =>
            {
                md5.Initialize();
                using(var fs = System.IO.File.OpenRead(path))
                    return Convert.ToBase64String(md5.ComputeHash(fs));
            };

            Func<string, string> computeSHA256 = (path) =>
            {
                sha256.Initialize();
                using(var fs = System.IO.File.OpenRead(path))
                    return Convert.ToBase64String(sha256.ComputeHash(fs));
            };

            // Build a zip
            using (var archive_temp = new Duplicati.Library.Utility.TempFile())
            {
                using (var zipfile = new Duplicati.Library.Compression.FileArchiveZip(archive_temp, new Dictionary<string, string>()))
                {
                    Func<string, string, bool> addToArchive = (path, relpath) =>
                    {
                        if (ignoreMap.ContainsKey(relpath))
                            return false;
                    
                        if (path.EndsWith(dirsep))
                            return true;

                        using (var source = System.IO.File.OpenRead(path))
                        using (var target = zipfile.CreateFile(relpath, 
                                           Duplicati.Library.Interface.CompressionHint.Compressible,
                                           System.IO.File.GetLastAccessTimeUtc(path)))
                        {
                            source.CopyTo(target);
                            remoteManifest.UncompressedSize += source.Length;
                        }

                        return true;
                    };
                        
                    // Build the update manifest
                    localManifest.Files =
                (from fse in Duplicati.Library.Utility.Utility.EnumerateFileSystemEntries(inputfolder)
                                let relpath = fse.Substring(baselen)
                                where addToArchive(fse, relpath)
                                select new FileEntry() {
                        Path = relpath,
                        LastWriteTime = System.IO.File.GetLastAccessTimeUtc(fse),
                        MD5 = fse.EndsWith(dirsep) ? null : computeMD5(fse),
                        SHA256 = fse.EndsWith(dirsep) ? null : computeSHA256(fse)
                    })
                .Union(ignoreFiles).ToArray();

                    // Write a signed manifest with the files
                
                        using (var ms = new System.IO.MemoryStream())
                        using (var sw = new System.IO.StreamWriter(ms))
                        {
                            new Newtonsoft.Json.JsonSerializer().Serialize(sw, localManifest);
                            sw.Flush();

                            using (var ms2 = new System.IO.MemoryStream())
                            {
                                SignatureReadingStream.CreateSignedStream(ms, ms2, key);
                                ms2.Position = 0;
                                using (var sigfile = zipfile.CreateFile(UPDATE_MANIFEST_FILENAME, 
                                    Duplicati.Library.Interface.CompressionHint.Compressible,
                                    DateTime.UtcNow))
                                    ms2.CopyTo(sigfile);

                            }
                        }
                }

                remoteManifest.CompressedSize = new System.IO.FileInfo(archive_temp).Length;
                remoteManifest.MD5 = computeMD5(archive_temp);
                remoteManifest.SHA256 = computeSHA256(archive_temp);

                System.IO.File.Move(archive_temp, System.IO.Path.Combine(outputfolder, "package.zip"));

            }

            // Write a signed manifest for upload

            using(var tf = new Duplicati.Library.Utility.TempFile())
            {
                using (var ms = new System.IO.MemoryStream())
                using (var sw = new System.IO.StreamWriter(ms))
                {
                    new Newtonsoft.Json.JsonSerializer().Serialize(sw, remoteManifest);
                    sw.Flush();

                    using (var fs = System.IO.File.OpenWrite(tf))
                        SignatureReadingStream.CreateSignedStream(ms, fs, key);
                }

                System.IO.File.Move(tf, System.IO.Path.Combine(outputfolder, UPDATE_MANIFEST_FILENAME));
            }

        }

        private int RunMethod(System.Reflection.MethodInfo method, string[] args)
        {
            try
            {
                var n = method.Invoke(null, new object[] { args });
                if (method.ReturnType == typeof(int))
                    return (int)n;

                return 0;
            } 
            catch (System.Reflection.TargetInvocationException tex)
            {
                if (tex.InnerException != null)
                    throw tex.InnerException;
                else
                    throw;
            }
        }

        public int RunFromMostRecent(System.Reflection.MethodInfo method, string[] cmdargs)
        {
            // If we are not the primary domain, just execute
            if (!AppDomain.CurrentDomain.IsDefaultAppDomain())
                return RunMethod(method, cmdargs);
            
            // Check if there are updates, otherwise use
            var best = FindInstalledVersions().OrderBy(x => x.Value.ReleaseTime).FirstOrDefault();
            if (best.Key == null && !VerifyUnpackedFolder(best.Key, best.Value))
                best = new KeyValuePair<string, UpdateInfo>(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, null);
            
            Environment.SetEnvironmentVariable(string.Format(INSTALLDIR_ENVNAME_TEMPLATE, m_appname), InstalledBaseDir);

            var folder = best.Key;

            // Basic idea with the loop is that the running AppDomain can use 
            // RUN_UPDATED_ENVNAME_TEMPLATE to signal that a new version is ready
            // when the caller exits, the new update is executed
            //
            // This allows more or less seamless updates
            //
            // The client is responsible for checking for updates and starting the downloads
            //

            int result = 0;
            while (!string.IsNullOrWhiteSpace(folder) && System.IO.Directory.Exists(folder))
            {
                var prevfolder = folder;
                // Create the new domain
                var domain = AppDomain.CreateDomain(
                    "UpdateDomain",
                    null,
                    folder,
                    "",
                    false
                );

                result = domain.ExecuteAssemblyByName(method.DeclaringType.Assembly.GetName().Name, cmdargs);

                AppDomain.Unload(domain);

                folder = Environment.GetEnvironmentVariable(string.Format(RUN_UPDATED_ENVNAME_TEMPLATE, m_appname));
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Environment.SetEnvironmentVariable(string.Format(RUN_UPDATED_ENVNAME_TEMPLATE, m_appname), null);
                    if (!VerifyUnpackedFolder(folder))
                        folder = prevfolder; //Go back and run the previous version
                }
            }

            return result;
        }
    }
}

