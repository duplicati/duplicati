//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal static class FilelistProcessor
    {
		/// <summary>
		/// Helper method that verifies uploaded volumes and updates their state in the database.
		/// Throws an error if there are issues with the remote storage
		/// </summary>
        /// <param name="backend">The backend instance to use</param>
		/// <param name="database">The database to compare with</param>
        public static async Task VerifyLocalListAsync(Common.BackendHandler backend, LocalDatabase database)
		{
            using (var log = new Common.LogWrapper())
            {
                var locallist = database.GetRemoteVolumes();
                foreach (var i in locallist)
                {
                    switch (i.State)
                    {
                        case RemoteVolumeState.Uploaded:
                        case RemoteVolumeState.Verified:
                        case RemoteVolumeState.Deleted:
                            break;

                        case RemoteVolumeState.Temporary:
                        case RemoteVolumeState.Deleting:
                        case RemoteVolumeState.Uploading:
                            await log.WriteInformationAsync(string.Format("removing remote file listed as {0}: {1}", i.State, i.Name));
                            try
                            {
                                await backend.DeleteFileAsync(i.Name, true);
                            }
                            catch (Exception ex)
                            {
                                await log.WriteWarningAsync(string.Format("Failed to erase file {0}, treating as deleted: {1}", i.Name, ex.Message), ex);
                            }

                            break;

                        default:
                            await log.WriteWarningAsync(string.Format("unknown state for remote file listed as {0}: {1}", i.State, i.Name), null);
                            break;
                    }

                }
            }
		}

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        /// <param name="log">The log instance to use</param>
        public static void VerifyLocalList(BackendManager backend, Options options, LocalDatabase database, IBackendWriter log)
        {
            var locallist = database.GetRemoteVolumes();
            foreach(var i in locallist)
            {
                switch (i.State)
                {
                    case RemoteVolumeState.Uploaded:
                    case RemoteVolumeState.Verified:
                    case RemoteVolumeState.Deleted:
                        break;

                    case RemoteVolumeState.Temporary:
                    case RemoteVolumeState.Deleting:
                    case RemoteVolumeState.Uploading:
                        log.AddMessage(string.Format("removing remote file listed as {0}: {1}", i.State, i.Name));
                        try
                        {
                            backend.Delete(i.Name, i.Size, true);
                        }
                        catch (Exception ex)
                        {
                            log.AddWarning(string.Format("Failed to erase file {0}, treating as deleted: {1}", i.Name, ex.Message), ex);
                        }

                        break;

                    default:
                        log.AddWarning(string.Format("unknown state for remote file listed as {0}: {1}", i.State, i.Name), null);
                        break;
                }

                backend.FlushDbMessages();
            }
        }

		/// <summary>
		/// Helper method that verifies uploaded volumes and updates their state in the database.
		/// Throws an error if there are issues with the remote storage
		/// </summary>
		/// <param name="backend">The backend instance to use</param>
		/// <param name="options">The options used</param>
		/// <param name="database">The database to compare with</param>
		/// <param name="protectedfile">A filename that should be excempted for deletion</param>
        public static async Task VerifyRemoteListAsync(Common.BackendHandler backend, Options options, Common.DatabaseCommon database, Common.StatsCollector stats, string protectedfile = null)
		{
            using (var log = new Common.LogWrapper())
            {
                var tp = await RemoteListAnalysisAsync(backend, options, database, stats, protectedfile);
                long extraCount = 0;
                long missingCount = 0;

                foreach (var n in tp.ExtraVolumes)
                {
                    await log.WriteWarningAsync(string.Format("Extra unknown file: {0}", n.File.Name), null);
                    extraCount++;
                }

                foreach (var n in tp.MissingVolumes)
                {
                    await log.WriteWarningAsync(string.Format("Missing file: {0}", n.Name), null);
                    missingCount++;
                }

                if (extraCount > 0)
                {
                    var s = string.Format("Found {0} remote files that are not recorded in local storage, please run repair", extraCount);
                    await log.WriteErrorAsync(s, null);
                    throw new Duplicati.Library.Interface.UserInformationException(s);
                }

                var lookup = new HashSet<string>();
                var doubles = new HashSet<string>();
                foreach (var v in tp.ParsedVolumes)
                {
                    if (!lookup.Add(v.File.Name))
                        doubles.Add(v.File.Name);
                }

                if (doubles.Count > 0)
                {
                    var s = string.Format("Found remote files reported as duplicates, either the backend module is broken or you need to manually remove the extra copies.\nThe following files were found multiple times: {0}", string.Join(", ", doubles));
                    await log.WriteErrorAsync(s, null);
                    throw new Duplicati.Library.Interface.UserInformationException(s);
                }

                if (missingCount > 0)
                {
                    string s;
                    if (!tp.BackupPrefixes.Contains(options.Prefix) && tp.BackupPrefixes.Length > 0)
                        s = string.Format("Found {0} files that are missing from the remote storage, and no files with the backup prefix {1}, but found the following backup prefixes: {2}", missingCount, options.Prefix, string.Join(", ", tp.BackupPrefixes));
                    else
                        s = string.Format("Found {0} files that are missing from the remote storage, please run repair", missingCount);

                    await log.WriteErrorAsync(s, null);
                    throw new Duplicati.Library.Interface.UserInformationException(s);
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
        /// <param name="log">The log instance to use</param>
        /// <param name="protectedfile">A filename that should be excempted for deletion</param>
        public static void VerifyRemoteList(BackendManager backend, Options options, LocalDatabase database, IBackendWriter log, string protectedfile = null)
        {
            var tp = RemoteListAnalysis(backend, options, database, log, protectedfile);
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
                throw new Duplicati.Library.Interface.UserInformationException(s);
            }

            var lookup = new HashSet<string>();
            var doubles = new HashSet<string>();
            foreach(var v in tp.ParsedVolumes)
            {
                if (!lookup.Add(v.File.Name))
                    doubles.Add(v.File.Name);
            }

            if (doubles.Count > 0)
            {
                var s = string.Format("Found remote files reported as duplicates, either the backend module is broken or you need to manually remove the extra copies.\nThe following files were found multiple times: {0}", string.Join(", ", doubles));
                log.AddError(s, null);
                throw new Duplicati.Library.Interface.UserInformationException(s);
            }

            if (missingCount > 0)
            {
                string s;
                if (!tp.BackupPrefixes.Contains(options.Prefix) && tp.BackupPrefixes.Length > 0)
                    s = string.Format("Found {0} files that are missing from the remote storage, and no files with the backup prefix {1}, but found the following backup prefixes: {2}", missingCount, options.Prefix, string.Join(", ", tp.BackupPrefixes));
                else
                    s = string.Format("Found {0} files that are missing from the remote storage, please run repair", missingCount);
                
                log.AddError(s, null);
                throw new Duplicati.Library.Interface.UserInformationException(s);
            }            
        }

        public struct RemoteAnalysisResult
        {
            public IEnumerable<Volumes.IParsedVolume> ParsedVolumes;
            public IEnumerable<Volumes.IParsedVolume> ExtraVolumes;
            public IEnumerable<Volumes.IParsedVolume> OtherVolumes;
            public IEnumerable<RemoteVolumeEntry> MissingVolumes;
            public IEnumerable<RemoteVolumeEntry> VerificationRequiredVolumes;

            public string[] BackupPrefixes { get { return ParsedVolumes.Union(ExtraVolumes).Union(OtherVolumes).Select(x => x.Prefix).Distinct().ToArray(); } }
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
            s.Serialize(stream, db.GetRemoteVolumes().Where(x => x.State != RemoteVolumeState.Temporary).Cast<IRemoteVolume>().ToArray());
        }

		/// <summary>
		/// Creates a temporary verification file.
		/// </summary>
		/// <returns>The verification file.</returns>
		/// <param name="db">The database instance</param>
		/// <param name="stream">The stream to write to</param>
        public static async Task CreateVerificationFileAsync(Common.DatabaseCommon db, System.IO.StreamWriter stream)
		{
			var s = new Newtonsoft.Json.JsonSerializer();
            s.Serialize(stream, (await db.GetRemoteVolumesAsync()).Where(x => x.State != RemoteVolumeState.Temporary).Cast<IRemoteVolume>().ToArray());
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
		/// Uploads the verification file.
		/// </summary>
		/// <param name="backend">The backend to use</param>
		/// <param name="options">The options to use</param>
		/// <param name="db">The database to read from</param>
        public static async Task UploadVerificationFileAsync(Common.BackendHandler backend, Options options, Common.DatabaseCommon db)
		{
			using (var tempfile = new Library.Utility.TempFile())
			{
				var remotename = options.Prefix + "-verification.json";
				using (var stream = new System.IO.StreamWriter(tempfile, false, System.Text.Encoding.UTF8))
					await FilelistProcessor.CreateVerificationFileAsync(db, stream);

				if (options.Dryrun)
				{
                    using(var log = new Common.LogWrapper())
                        await log.WriteDryRunAsync(string.Format("Would upload verification file: {0}, size: {1}", remotename, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(tempfile).Length)));
				}
				else
				{
                    await backend.PutUnencryptedAsync(remotename, tempfile);
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
		/// <param name="protectedfile">A filename that should be excempted for deletion</param>
        public static async Task<RemoteAnalysisResult> RemoteListAnalysisAsync(Common.BackendHandler backend, Options options, Common.DatabaseCommon database, Common.StatsCollector stats, string protectedfile)
		{
            using (var log = new Common.LogWrapper())
            {
                var rawlist = await backend.ListFilesAsync();
                var lookup = new Dictionary<string, Volumes.IParsedVolume>();
                protectedfile = protectedfile ?? string.Empty;

                var remotelist = new List<Volumes.IParsedVolume>();
                var otherlist = new List<Volumes.IParsedVolume>();
                var unknownlist = new List<Interface.IFileEntry>();

                foreach (var n in rawlist)
                {
                    var p = Volumes.VolumeBase.ParseFilename(n);
                    if (p == null)
                    {
                        unknownlist.Add(n);
                    }
                    else
                    {
                        if (p.Prefix != options.Prefix)
                            otherlist.Add(p);
                        else
                            remotelist.Add(p);
                    }
                }

                var filesets = (from n in remotelist
                                where n.FileType == RemoteVolumeType.Files
                                orderby n.Time descending
                                select n).ToList();

                stats.KnownFileCount = remotelist.Count;
                stats.KnownFileSize = remotelist.Select(x => Math.Max(0, x.File.Size)).Sum();
                stats.UnknownFileCount = unknownlist.Count;
                stats.UnknownFileSize = unknownlist.Select(x => Math.Max(0, x.Size)).Sum();
                stats.BackupListCount = filesets.Count;
                stats.LastBackupDate = filesets.Count == 0 ? new DateTime(0) : filesets[0].Time.ToLocalTime();

                // TODO: We should query through the backendmanager
                var quota = await backend.GetQuotaAsync();
                if (quota != null)
                {
                    stats.TotalQuotaSpace = quota.TotalQuotaSpace;
                    stats.FreeQuotaSpace = quota.FreeQuotaSpace;
                }

                stats.AssignedQuotaSpace = options.QuotaSize;

                foreach (var s in remotelist)
                    lookup[s.File.Name] = s;

                var missing = new List<RemoteVolumeEntry>();
                var missingHash = new List<Tuple<long, RemoteVolumeEntry>>();
                var cleanupRemovedRemoteVolumes = new HashSet<string>();

                foreach (var e in await database.DuplicateRemoteVolumesAsync())
                {
                    if (e.Value == RemoteVolumeState.Uploading || e.Value == RemoteVolumeState.Temporary)
                        await database.UnlinkRemoteVolumeAsync(e.Key, e.Value);
                    else
                        throw new Exception(string.Format("The remote volume {0} appears in the database with state {1} and a deleted state, cannot continue", e.Key, e.Value.ToString()));
                }

                var locallist = await database.GetRemoteVolumesAsync();
                foreach (var i in locallist)
                {
                    Volumes.IParsedVolume r;
                    var remoteFound = lookup.TryGetValue(i.Name, out r);
                    var correctSize = remoteFound && i.Size >= 0 && (i.Size == r.File.Size || r.File.Size < 0);

                    lookup.Remove(i.Name);

                    switch (i.State)
                    {
                        case RemoteVolumeState.Deleted:
                            if (remoteFound)
                                await log.WriteInformationAsync(string.Format("ignoring remote file listed as {0}: {1}", i.State, i.Name));

                            break;

                        case RemoteVolumeState.Temporary:
                        case RemoteVolumeState.Deleting:
                            if (remoteFound)
                            {
                                await log.WriteInformationAsync(string.Format("removing remote file listed as {0}: {1}", i.State, i.Name));
                                await backend.DeleteFileAsync(i.Name, true);
                            }
                            else
                            {
                                if (i.DeleteGracePeriod > DateTime.UtcNow)
                                {
                                    await log.WriteInformationAsync(string.Format("keeping delete request for {0} until {1}", i.Name, i.DeleteGracePeriod.ToLocalTime()));
                                }
                                else
                                {
                                    if (string.Equals(i.Name, protectedfile) && i.State == RemoteVolumeState.Temporary)
                                    {
                                        await log.WriteInformationAsync(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                    }
                                    else
                                    {
                                        await log.WriteInformationAsync(string.Format("removing file listed as {0}: {1}", i.State, i.Name));
                                        cleanupRemovedRemoteVolumes.Add(i.Name);
                                    }
                                }
                            }
                            break;
                        case RemoteVolumeState.Uploading:
                            if (remoteFound && correctSize && r.File.Size >= 0)
                            {
                                await log.WriteInformationAsync(string.Format("promoting uploaded complete file from {0} to {2}: {1}", i.State, i.Name, RemoteVolumeState.Uploaded));
                                await database.UpdateRemoteVolumeAsync(i.Name, RemoteVolumeState.Uploaded, i.Size, i.Hash);
                            }
                            else if (!remoteFound)
                            {

                                if (string.Equals(i.Name, protectedfile))
                                {
                                    await log.WriteInformationAsync(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                    await database.UpdateRemoteVolumeAsync(i.Name, RemoteVolumeState.Temporary, i.Size, i.Hash, false, new TimeSpan(0));
                                }
                                else
                                {
                                    await log.WriteInformationAsync(string.Format("scheduling missing file for deletion, currently listed as {0}: {1}", i.State, i.Name));
                                    cleanupRemovedRemoteVolumes.Add(i.Name);
                                    await database.UpdateRemoteVolumeAsync(i.Name, RemoteVolumeState.Deleting, i.Size, i.Hash, false, TimeSpan.FromHours(2));
                                }
                            }
                            else
                            {
                                if (string.Equals(i.Name, protectedfile))
                                {
                                    await log.WriteInformationAsync(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                }
                                else
                                {
                                    await log.WriteInformationAsync(string.Format("removing incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                    await backend.DeleteFileAsync(i.Name, true);
                                }
                            }
                            break;

                        case RemoteVolumeState.Uploaded:
                            if (!remoteFound)
                                missing.Add(i);
                            else if (correctSize)
                                await database.UpdateRemoteVolumeAsync(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash);
                            else
                                missingHash.Add(new Tuple<long, RemoteVolumeEntry>(r.File.Size, i));

                            break;

                        case RemoteVolumeState.Verified:
                            if (!remoteFound)
                                missing.Add(i);
                            else if (!correctSize)
                                missingHash.Add(new Tuple<long, RemoteVolumeEntry>(r.File.Size, i));

                            break;

                        default:
                            await log.WriteWarningAsync(string.Format("unknown state for remote file listed as {0}: {1}", i.State, i.Name), null);
                            break;
                    }
                }

                // cleanup deleted volumes in DB en block
                await database.RemoveRemoteVolumesAsync(cleanupRemovedRemoteVolumes);

                foreach (var i in missingHash)
                    await log.WriteWarningAsync(string.Format("remote file {1} is listed as {0} with size {2} but should be {3}, please verify the sha256 hash \"{4}\"", i.Item2.State, i.Item2.Name, i.Item1, i.Item2.Size, i.Item2.Hash), null);

                return new RemoteAnalysisResult()
                {
                    ParsedVolumes = remotelist,
                    OtherVolumes = otherlist,
                    ExtraVolumes = lookup.Values,
                    MissingVolumes = missing,
                    VerificationRequiredVolumes = missingHash.Select(x => x.Item2)
                };
            }
        }

		/// <summary>
		/// Helper method that verifies uploaded volumes and updates their state in the database.
		/// Throws an error if there are issues with the remote storage
		/// </summary>
		/// <param name="rawlist">The list of files returned from the backend</param>
		/// <param name="options">The options used</param>
		/// <param name="database">The database to compare with</param>
		/// <param name="protectedfile">A filename that should be excempted for deletion</param>
		private static RemoteAnalysisResult RemoteListAnalysis(BackendManager backend, Options options, LocalDatabase database, IBackendWriter log, string protectedfile)
        {
            var rawlist = backend.List();
            var lookup = new Dictionary<string, Volumes.IParsedVolume>();
            protectedfile = protectedfile ?? string.Empty;

            var remotelist = new List<Volumes.IParsedVolume>();
			var otherlist = new List<Volumes.IParsedVolume>();
			var unknownlist = new List<Interface.IFileEntry>();

            foreach (var n in rawlist)
            {
                var p = Volumes.VolumeBase.ParseFilename(n);
                if (p == null)
                {
                    unknownlist.Add(n);
                }
                else
                {
                    if (p.Prefix != options.Prefix)
                        otherlist.Add(p);
                    else
                        remotelist.Add(p);
                }
            }

            var filesets = (from n in remotelist
                                     where n.FileType == RemoteVolumeType.Files orderby n.Time descending
                                     select n).ToList();

            log.KnownFileCount = remotelist.Count;
            log.KnownFileSize = remotelist.Select(x => Math.Max(0, x.File.Size)).Sum();
            log.UnknownFileCount = unknownlist.Count;
            log.UnknownFileSize = unknownlist.Select(x => Math.Max(0, x.Size)).Sum();
            log.BackupListCount = filesets.Count;
            log.LastBackupDate = filesets.Count == 0 ? new DateTime(0) : filesets[0].Time.ToLocalTime();

            // TODO: We should query through the backendmanager
            using (var bk = DynamicLoader.BackendLoader.GetBackend(backend.BackendUrl, options.RawOptions))
                if (bk is Library.Interface.IQuotaEnabledBackend)
                {
                    Library.Interface.IQuotaInfo quota = ((Library.Interface.IQuotaEnabledBackend)bk).Quota;
                    if (quota != null)
                    {
                        log.TotalQuotaSpace = quota.TotalQuotaSpace;
                        log.FreeQuotaSpace = quota.FreeQuotaSpace;
                    }
                }

            log.AssignedQuotaSpace = options.QuotaSize;
            
            foreach(var s in remotelist)
                lookup[s.File.Name] = s;
                    
            var missing = new List<RemoteVolumeEntry>();
            var missingHash = new List<Tuple<long, RemoteVolumeEntry>>();
            var cleanupRemovedRemoteVolumes = new HashSet<string>();

            foreach(var e in database.DuplicateRemoteVolumes())
            {
                if (e.Value == RemoteVolumeState.Uploading || e.Value == RemoteVolumeState.Temporary)
                    database.UnlinkRemoteVolume(e.Key, e.Value);
                else
                    throw new Exception(string.Format("The remote volume {0} appears in the database with state {1} and a deleted state, cannot continue", e.Key, e.Value.ToString()));
            }

            var locallist = database.GetRemoteVolumes();
            foreach(var i in locallist)
            {
                Volumes.IParsedVolume r;
                var remoteFound = lookup.TryGetValue(i.Name, out r);
                var correctSize = remoteFound && i.Size >= 0 && (i.Size == r.File.Size || r.File.Size < 0);

                lookup.Remove(i.Name);

                switch (i.State)
                {
                    case RemoteVolumeState.Deleted:
                        if (remoteFound)
                            log.AddMessage(string.Format("ignoring remote file listed as {0}: {1}", i.State, i.Name));

                        break;

                    case RemoteVolumeState.Temporary:
                    case RemoteVolumeState.Deleting:
                        if (remoteFound)
                        {
                            log.AddMessage(string.Format("removing remote file listed as {0}: {1}", i.State, i.Name));
                            backend.Delete(i.Name, i.Size, true);
                        }
                        else
                        {
                            if (i.DeleteGracePeriod > DateTime.UtcNow)
                            {
                                log.AddMessage(string.Format("keeping delete request for {0} until {1}", i.Name, i.DeleteGracePeriod.ToLocalTime()));
                            }
                            else
                            {
                                if (string.Equals(i.Name, protectedfile) && i.State == RemoteVolumeState.Temporary)
                                {
                                    log.AddMessage(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                }
                                else
                                {
                                    log.AddMessage(string.Format("removing file listed as {0}: {1}", i.State, i.Name));
                                    cleanupRemovedRemoteVolumes.Add(i.Name);
                                }
                            }
                        }
                        break;
                    case RemoteVolumeState.Uploading:
                        if (remoteFound && correctSize && r.File.Size >= 0)
                        {
                            log.AddMessage(string.Format("promoting uploaded complete file from {0} to {2}: {1}", i.State, i.Name, RemoteVolumeState.Uploaded));
                            database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Uploaded, i.Size, i.Hash);
                        }
                        else if (!remoteFound)
                        {

                            if (string.Equals(i.Name, protectedfile))
                            {
                                log.AddMessage(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Temporary, i.Size, i.Hash, false, new TimeSpan(0), null);
                            }
                            else
                            {
                                log.AddMessage(string.Format("scheduling missing file for deletion, currently listed as {0}: {1}", i.State, i.Name));
                                cleanupRemovedRemoteVolumes.Add(i.Name);
                                database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Deleting, i.Size, i.Hash, false, TimeSpan.FromHours(2), null);
                            }
                        }
                        else
                        {
                            if (string.Equals(i.Name, protectedfile))
                            {
                                log.AddMessage(string.Format("keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name));
                            }
                            else
                            {
                                log.AddMessage(string.Format("removing incomplete remote file listed as {0}: {1}", i.State, i.Name));
                                backend.Delete(i.Name, i.Size, true);
                            }
                        }
                        break;

                    case RemoteVolumeState.Uploaded:
                        if (!remoteFound)
                            missing.Add(i);
                        else if (correctSize)
                            database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash);
                        else
                            missingHash.Add(new Tuple<long, RemoteVolumeEntry>(r.File.Size, i));

                        break;

                    case RemoteVolumeState.Verified:
                        if (!remoteFound)
                            missing.Add(i);
                        else if (!correctSize)
                            missingHash.Add(new Tuple<long, RemoteVolumeEntry>(r.File.Size, i));

                        break;
                
                    default:
                        log.AddWarning(string.Format("unknown state for remote file listed as {0}: {1}", i.State, i.Name), null);
                        break;
                }

                backend.FlushDbMessages();
            }

            // cleanup deleted volumes in DB en block
            database.RemoveRemoteVolumes(cleanupRemovedRemoteVolumes, null);

            foreach(var i in missingHash)
                log.AddWarning(string.Format("remote file {1} is listed as {0} with size {2} but should be {3}, please verify the sha256 hash \"{4}\"", i.Item2.State, i.Item2.Name, i.Item1, i.Item2.Size, i.Item2.Hash), null);
            
            return new RemoteAnalysisResult() { 
                ParsedVolumes = remotelist, 
                OtherVolumes = otherlist,
                ExtraVolumes = lookup.Values, 
                MissingVolumes = missing, 
                VerificationRequiredVolumes = missingHash.Select(x => x.Item2) 
            };
        }
    }    
}

