//  Copyright (C) 2011, Kenneth Skovhede

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
using Duplicati.Library.Main.Database;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Main.Operation
{
    internal static class FilelistProcessor
    {
        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        public static void VerifyRemoteList(BackendManager backend, Options options, LocalDatabase database, IBackendWriter log)
		{
			var tp = RemoteListAnalysis(backend, options, database, log);
			long extraCount = 0;
			long missingCount = 0;
            
			foreach(var n in tp.ExtraVolumes)
			{
				log.AddWarning(string.Format("Extra unknown file: {0}", n.File.Name), null);
				extraCount++;
			}

			foreach(var n in tp.MissingVolumes)
			{
				log.AddWarning(string.Format("Missing file: {0}", n.Name), null);
				missingCount++;
			}

			if (extraCount > 0)
			{
				var s = string.Format("Found {0} remote files that are not recorded in local storage, please run repair", extraCount);
				log.AddError(s, null);
				throw new Exception(s);
			}

            if (missingCount > 0)
            {
            	string s;
                if (!tp.BackupPrefixes.Contains(options.Prefix) && tp.BackupPrefixes.Length > 0)
                	s = string.Format("Found {0} files that are missing from the remote storage, and no files with the backup prefix {1}, but found the following backup prefixes: {2}", missingCount, options.Prefix, string.Join(", ", tp.BackupPrefixes));
                else
                	s = string.Format("Found {0} files that are missing from the remote storage, please run repair", missingCount);
                
                log.AddError(s, null);
                throw new Exception(s);
            }            
        }

        public struct RemoteAnalysisResult
        {
            public IEnumerable<Volumes.IParsedVolume> ParsedVolumes;
            public IEnumerable<Volumes.IParsedVolume> ExtraVolumes;
            public IEnumerable<RemoteVolumeEntry> MissingVolumes;
            
            public string[] BackupPrefixes { get { return ParsedVolumes.Select(x => x.Prefix).Distinct().ToArray(); } }
        }

        /// <summary>
        /// Creates a temporary verification file.
        /// </summary>
        /// <returns>The verification file.</returns>
        /// <param name="db">The database instance</param>
        /// <param name="stream">The stream to write to</param>
        public static void CreateVerificationFile(LocalDatabase db, System.IO.StreamWriter stream)
        {
            var s = new Newtonsoft.Json.JsonSerializer();
            s.Serialize(stream, db.GetRemoteVolumes().Cast<IRemoteVolume>().ToArray());
        }
        
        /// <summary>
        /// Uploads the verification file.
        /// </summary>
        /// <param name="backendurl">The backend url</param>
        /// <param name="options">The options to use</param>
        /// <param name="result">The result writer</param>
        /// <param name="db">The attached database</param>
        /// <param name="transaction">An optional transaction object</param>
        public static void UploadVerificationFile(string backendurl, Options options, IBackendWriter result, LocalDatabase db, System.Data.IDbTransaction transaction)
        {
            using(var backend = new BackendManager(backendurl, options, result, db))
            using(var tempfile = new Library.Utility.TempFile())
            {
                var remotename = options.Prefix + "-verification.json";
                using(var stream = new System.IO.StreamWriter(tempfile, false, System.Text.Encoding.UTF8))
                    FilelistProcessor.CreateVerificationFile(db, stream);
                    
                if (options.Dryrun)
                {
                    result.AddDryrunMessage(string.Format("Would upload verification file: {0}, size: {1}", remotename, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(tempfile).Length)));
                }
                else
                {
                    backend.PutUnencrypted(remotename, tempfile);
                    backend.WaitForComplete(db, transaction);
                }
            }
        }

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        public static RemoteAnalysisResult RemoteListAnalysis(BackendManager backend, Options options, LocalDatabase database, IBackendWriter log)
        {
            var rawlist = backend.List();
            var lookup = new Dictionary<string, Volumes.IParsedVolume>();

            var remotelist = (from n in rawlist let p = Volumes.VolumeBase.ParseFilename(n) where p != null select p).ToList();
            var unknownlist = (from n in rawlist let p = Volumes.VolumeBase.ParseFilename(n) where p == null select n).ToList();
            var filesets = (from n in remotelist where n.FileType == RemoteVolumeType.Files orderby n.Time descending select n).ToList();
            
            log.KnownFileCount = remotelist.Count();
            log.KnownFileSize = remotelist.Select(x => x.File.Size).Sum();
            log.UnknownFileCount = unknownlist.Count();
            log.UnknownFileSize = unknownlist.Select(x => x.Size).Sum();
            log.BackupListCount = filesets.Count;
            log.LastBackupDate = filesets.Count == 0 ? new DateTime(0) : filesets[0].Time.ToLocalTime();
            
            if (backend is Library.Interface.IQuotaEnabledBackend)
            {
                log.TotalQuotaSpace = ((Library.Interface.IQuotaEnabledBackend)backend).TotalQuotaSpace;
                log.FreeQuotaSpace = ((Library.Interface.IQuotaEnabledBackend)backend).FreeQuotaSpace;
            }

            log.AssignedQuotaSpace = options.QuotaSize;
            
            foreach (var s in remotelist)
                if (s.Prefix == options.Prefix)
                    lookup[s.File.Name] = s;
                    
            var missing = new List<RemoteVolumeEntry>();
            var locallist = database.GetRemoteVolumes();
            foreach (var i in locallist)
            {
                //Ignore those that are deleted
                if (i.State == RemoteVolumeState.Deleted)
                    continue;
                    
                if (i.State == RemoteVolumeState.Temporary)
                {
                    log.AddMessage(string.Format("removing file listed as {0}: {1}", i.State, i.Name));
                    database.RemoveRemoteVolume(i.Name, null);
                }
                else if (i.State == RemoteVolumeState.Deleting && lookup.ContainsKey(i.Name))
                {
                    log.AddMessage(string.Format("removing remote file listed as {0}: {1}", i.State, i.Name));
                    backend.Delete(i.Name, i.Size, true);
                    lookup.Remove(i.Name);
                }
                else
                {
                    Volumes.IParsedVolume r;
                    if (!lookup.TryGetValue(i.Name, out r))
                    {
                        if (i.State == RemoteVolumeState.Uploading || i.State == RemoteVolumeState.Deleting || (r != null && r.File.Size != i.Size && r.File.Size >= 0 && i.Size >= 0))
                        {
                            log.AddMessage(string.Format("removing file listed as {0}: {1}", i.State, i.Name));
                            database.RemoveRemoteVolume(i.Name, null);
                        }
                        else
                            missing.Add(i);
                    }
                    else if (i.State != RemoteVolumeState.Verified)
                    {
                        database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash);
                    }

                    lookup.Remove(i.Name);
                }
            }
            
            return new RemoteAnalysisResult() { ParsedVolumes = remotelist, ExtraVolumes = lookup.Values, MissingVolumes = missing };
        }
        
        internal static IEnumerable<Volumes.IParsedVolume> ParseFileList(string target, Dictionary<string, string> options, IBackendWriter log)
        {
            var opts = new Options(options);
            using (var db = new LocalDatabase(opts.Dbpath, "ParseFileList"))
            using (var b = new BackendManager(target, opts, log, db))
            {
                var res = 
                    from n in b.List()
                    let np = Volumes.VolumeBase.ParseFilename(n)
                    where np != null
                    select np;
                    
                b.WaitForComplete(db, null);
                
                return res;
            }
        }    
    }    
}

