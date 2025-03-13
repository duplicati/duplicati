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
using Duplicati.Library.Main.Database;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Duplicati.Library.Interface;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal static class FilelistProcessor
    {
        /// <summary>
        /// The approach to take when verifying the filelist
        /// </summary>
        public enum VerifyMode
        {
            /// <summary>
            /// Just verify the list, do not repair partials
            /// </summary>
            VerifyOnly,
            /// <summary>
            /// Verify the list and fail if there are partials
            /// </summary>
            VerifyStrict,
            /// <summary>
            /// Verify the list and repair partials
            /// </summary>
            VerifyAndClean,
            /// <summary>
            /// Force a verification and repair, even if the last operation was successful
            /// </summary>
            VerifyAndCleanForced
        }


        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(FilelistProcessor));

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="database">The database to compare with</param>
        public static async Task VerifyLocalList(IBackendManager backendManager, LocalDatabase database, CancellationToken cancellationToken)
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
                        Logging.Log.WriteInformationMessage(LOGTAG, "RemovingStaleFile", "Removing remote file listed as {0}: {1}", i.State, i.Name);
                        try
                        {
                            await backendManager.DeleteAsync(i.Name, i.Size, true, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFileFailed", ex, "Failed to erase file {0}, treating as deleted: {1}", i.Name, ex.Message);
                        }

                        break;

                    default:
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnknownFileState", null, "Unknown state for remote file listed as {0}: {1}", i.State, i.Name);
                        break;
                }

                await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        /// <param name="backendWriter">The backend writer to use</param>
        /// <param name="latestVolumesOnly">True if only the latest volumes should be verified</param>
        /// <param name="verifyMode">The mode to use for verification</param>
        /// <param name="transaction">An optional transaction object</param>
        /// <returns>An awaitable task</returns>
        public static async Task VerifyRemoteList(IBackendManager backend, Options options, LocalDatabase database, IBackendWriter backendWriter, bool latestVolumesOnly, VerifyMode verifyMode)
        {
            if (!options.NoBackendverification)
            {
                LocalBackupDatabase backupDatabase = new LocalBackupDatabase(database, options);
                IEnumerable<string> protectedFiles = backupDatabase.GetTemporaryFilelistVolumeNames(latestVolumesOnly);
                await VerifyRemoteList(backend, options, database, backendWriter, protectedFiles, [], logErrors: true, verifyMode: verifyMode).ConfigureAwait(false);
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
        /// <param name="protectedFiles">Filenames that should be exempted from deletion</param>
        /// <param name="strictExcemptFiles">Filenames that should be exempted from strict verification</param>
        /// <param name="verifyMode">The mode to use for verification</param>
        /// <param name="logErrors">Disable the logging of errors to prevent spamming the log; exceptions will be thrown regardless</param>
        /// <returns>An awaitable task</returns>
        public static async Task VerifyRemoteList(IBackendManager backend, Options options, LocalDatabase database, IBackendWriter log, IEnumerable<string> protectedFiles, IEnumerable<string> strictExcemptFiles, bool logErrors, VerifyMode verifyMode)
        {
            var tp = await RemoteListAnalysis(backend, options, database, log, protectedFiles, strictExcemptFiles, verifyMode).ConfigureAwait(false);
            long extraCount = 0;
            long missingCount = 0;

            foreach (var n in tp.ExtraVolumes)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ExtraUnknownFile", null, "Extra unknown file: {0}", n.File.Name);
                extraCount++;
            }

            foreach (var n in tp.MissingVolumes)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MissingFile", null, "Missing file: {0}", n.Name);
                missingCount++;
            }

            if (extraCount > 0)
            {
                var s = string.Format("Found {0} remote files that are not recorded in local storage, please run repair", extraCount);
                if (logErrors)
                    Logging.Log.WriteErrorMessage(LOGTAG, "ExtraRemoteFiles", null, s);
                throw new RemoteListVerificationException(s, "ExtraRemoteFiles");
            }

            ISet<string> doubles;
            Library.Utility.Utility.GetUniqueItems(tp.ParsedVolumes.Select(x => x.File.Name), out doubles);

            if (doubles.Count > 0)
            {
                var s = string.Format("Found remote files reported as duplicates, either the backend module is broken or you need to manually remove the extra copies.\nThe following files were found multiple times: {0}", string.Join(", ", doubles));
                if (logErrors)
                    Logging.Log.WriteErrorMessage(LOGTAG, "DuplicateRemoteFiles", null, s);
                throw new RemoteListVerificationException(s, "DuplicateRemoteFiles");
            }

            if (missingCount > 0)
            {
                string s;
                if (!tp.BackupPrefixes.Contains(options.Prefix) && tp.BackupPrefixes.Length > 0)
                    s = string.Format("Found {0} files that are missing from the remote storage, and no files with the backup prefix {1}, but found the following backup prefixes: {2}", missingCount, options.Prefix, string.Join(", ", tp.BackupPrefixes));
                else
                    s = string.Format("Found {0} files that are missing from the remote storage, please run repair", missingCount);

                if (logErrors)
                    Logging.Log.WriteErrorMessage(LOGTAG, "MissingRemoteFiles", null, s);
                throw new RemoteListVerificationException(s, "MissingRemoteFiles");
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
        /// Uploads the verification file.
        /// </summary>
        /// <param name="backendurl">The backend url</param>
        /// <param name="options">The options to use</param>
        /// <param name="db">The attached database</param>
        public static async Task UploadVerificationFile(IBackendManager backendManager, Options options, LocalDatabase db)
        {
            using (var tempfile = new Library.Utility.TempFile())
            {
                var remotename = options.Prefix + "-verification.json";
                await using (var stream = new System.IO.StreamWriter(tempfile, false, System.Text.Encoding.UTF8))
                    CreateVerificationFile(db, stream);

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadVerificationFile", "Would upload verification file: {0}, size: {1}", remotename, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(tempfile).Length));
                }
                else
                {
                    await backendManager.PutVerificationFileAsync(remotename, tempfile, CancellationToken.None).ConfigureAwait(false);
                    await backendManager.WaitForEmptyAsync(db, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Container for keeping updates of remote volumes
        /// </summary>
        /// <param name="Name">The name of the remote volume</param>
        /// <param name="State">The state of the remote volume</param>
        /// <param name="Size">The size of the remote volume</param>
        /// <param name="Hash">The hash of the remote volume</param>
        /// <param name="SuppressCleanup">True if the volume should not be cleaned up</param>
        /// <param name="GraceTime">The grace time for the volume</param>
        private sealed record RemoteVolumeUpdate(string Name, RemoteVolumeState State, long Size, string Hash, bool SuppressCleanup, TimeSpan GraceTime);

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        /// <param name="protectedFiles">Filenames that should be exempted from deletion</param>
        /// <param name="strictExcemptFiles">Filenames that should be exempted from strict verification</param>
        /// <param name="verifyMode">The mode to use for verification</param>
        /// <returns>The result of the analysis</returns>
        public static async Task<RemoteAnalysisResult> RemoteListAnalysis(IBackendManager backendManager, Options options, LocalDatabase database, IBackendWriter log, IEnumerable<string> protectedFiles, IEnumerable<string> strictExcemptFiles, VerifyMode verifyMode)
        {
            // If the last operation completed, no cleanup should be required
            if (verifyMode == VerifyMode.VerifyAndClean && !database.TerminatedWithActiveUploads)
                verifyMode = VerifyMode.VerifyStrict;

            // Force cleanup should set the mode to cleanup
            if (verifyMode == VerifyMode.VerifyAndCleanForced)
                verifyMode = VerifyMode.VerifyAndClean;

            var updateList = new List<RemoteVolumeUpdate>();
            var unlinkList = new List<KeyValuePair<string, RemoteVolumeState>>();
            var deleteList = new List<KeyValuePair<string, long>>();

            var rawlist = await backendManager.ListAsync(CancellationToken.None).ConfigureAwait(false);
            var lookup = new Dictionary<string, Volumes.IParsedVolume>();
            protectedFiles = protectedFiles ?? [];
            strictExcemptFiles = strictExcemptFiles ?? [];

            var remotelist = (from n in rawlist
                              let p = Volumes.VolumeBase.ParseFilename(n)
                              where p != null && p.Prefix == options.Prefix
                              select p).ToList();

            var otherlist = (from n in rawlist
                             let p = Volumes.VolumeBase.ParseFilename(n)
                             where p != null && p.Prefix != options.Prefix
                             select p).ToList();

            var unknownlist = (from n in rawlist
                               let p = Volumes.VolumeBase.ParseFilename(n)
                               where p == null
                               select n).ToList();

            var filesets = (from n in remotelist
                            where n.FileType == RemoteVolumeType.Files
                            orderby n.Time descending
                            select n).ToList();

            log.KnownFileCount = remotelist.Count;
            log.KnownFilesets = filesets.Count;
            long knownFileSize = remotelist.Select(x => Math.Max(0, x.File.Size)).Sum();
            log.KnownFileSize = knownFileSize;
            log.UnknownFileCount = unknownlist.Count;
            log.UnknownFileSize = unknownlist.Select(x => Math.Max(0, x.Size)).Sum();
            log.BackupListCount = database.FilesetTimes.Count();
            log.LastBackupDate = filesets.Count == 0 ? new DateTime(0) : filesets[0].Time.ToLocalTime();

            await CheckQuota(backendManager, options, log, knownFileSize).ConfigureAwait(false);

            foreach (var s in remotelist)
                lookup[s.File.Name] = s;

            var missing = new List<RemoteVolumeEntry>();
            var missingHash = new List<Tuple<long, RemoteVolumeEntry>>();
            var temporaryAndDeletedVolumes = new HashSet<string>();
            var missingUploadingVolumes = new HashSet<string>();

            foreach (var e in database.DuplicateRemoteVolumes())
            {
                if (e.Value == RemoteVolumeState.Uploading || e.Value == RemoteVolumeState.Temporary)
                    unlinkList.Add(e);
                else
                    throw new RemoteListVerificationException(string.Format("The remote volume {0} appears in the database with state {1} and a deleted state, cannot continue", e.Key, e.Value.ToString()), "AmbiguousStateRemoteFiles");
            }

            var locallist = database.GetRemoteVolumes();
            foreach (var i in locallist)
            {
                var remoteFound = lookup.TryGetValue(i.Name, out var r);
                var correctSize = remoteFound && i.Size >= 0 && (i.Size == r.File.Size || r.File.Size < 0);

                lookup.Remove(i.Name);

                switch (i.State)
                {
                    case RemoteVolumeState.Deleted:
                        if (remoteFound)
                            Logging.Log.WriteInformationMessage(LOGTAG, "IgnoreRemoteDeletedFile", "Ignoring remote file listed as {0}: {1}", i.State, i.Name);

                        break;

                    case RemoteVolumeState.Temporary:
                    case RemoteVolumeState.Deleting:
                        if (remoteFound)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFile", "Would delete remote file: {0}", i.Name);
                            }
                            else if (verifyMode == VerifyMode.VerifyOnly)
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "WouldRemoveUnwantedRemoteFile", "Would remove remote file listed as {0}: {1}, but defering as {2} is requested", i.State, i.Name, verifyMode);
                            }
                            else
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "RemoveUnwantedRemoteFile", "Removing remote file listed as {0}: {1}", i.State, i.Name);
                                deleteList.Add(new KeyValuePair<string, long>(i.Name, i.Size));
                            }
                        }
                        else
                        {
                            if (i.DeleteGracePeriod > DateTime.UtcNow)
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "KeepDeleteRequest", "Keeping delete request for {0} until {1}", i.Name, i.DeleteGracePeriod.ToLocalTime());
                            }
                            else
                            {
                                if (i.State == RemoteVolumeState.Temporary && protectedFiles.Any(pf => pf == i.Name))
                                {
                                    Logging.Log.WriteInformationMessage(LOGTAG, "KeepIncompleteFile", "Keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name);
                                }
                                else
                                {
                                    Logging.Log.WriteInformationMessage(LOGTAG, "RemoteUnwantedMissingFile", "Removing file listed as {0}: {1}", i.State, i.Name);
                                    temporaryAndDeletedVolumes.Add(i.Name);
                                }
                            }
                        }
                        break;
                    case RemoteVolumeState.Uploading:
                        if (remoteFound && correctSize && r.File.Size >= 0)
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "PromotingCompleteFile", "Promoting uploaded complete file from {0} to {2}: {1}", i.State, i.Name, RemoteVolumeState.Uploaded);
                            updateList.Add(new RemoteVolumeUpdate(i.Name, RemoteVolumeState.Uploaded, i.Size, i.Hash, false, new TimeSpan(0)));
                        }
                        else if (!remoteFound)
                        {
                            if (protectedFiles.Any(pf => pf == i.Name))
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "KeepIncompleteFile", "Keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name);
                                updateList.Add(new RemoteVolumeUpdate(i.Name, RemoteVolumeState.Temporary, i.Size, i.Hash, false, new TimeSpan(0)));
                            }
                            else if (verifyMode == VerifyMode.VerifyStrict && !strictExcemptFiles.Any(pf => pf == i.Name))
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "UnexpectedUploadingFile", null, "Unexpected file in uploading state: {0}", i.Name);
                                throw new RemoteListVerificationException($"The missing remote volume {i.Name} is in uploading state and strict mode is on", "UnexpectedUploadingFile");
                            }
                            else
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "SchedulingMissingFileForDelete", "Scheduling missing file for deletion, currently listed as {0}: {1}", i.State, i.Name);
                                missingUploadingVolumes.Add(i.Name);
                                updateList.Add(new RemoteVolumeUpdate(i.Name, RemoteVolumeState.Deleting, i.Size, i.Hash, false, TimeSpan.FromHours(2)));
                            }
                        }
                        else
                        {
                            if (protectedFiles.Any(pf => pf == i.Name))
                            {
                                Logging.Log.WriteInformationMessage(LOGTAG, "KeepIncompleteFile", "Keeping protected incomplete remote file listed as {0}: {1}", i.State, i.Name);
                            }
                            else if (verifyMode == VerifyMode.VerifyStrict && !strictExcemptFiles.Any(pf => pf == i.Name))
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "UnexpectedUploadingFile", null, "Unexpected file in uploading state: {0}", i.Name);
                                throw new RemoteListVerificationException($"The remote volume {i.Name} is in uploading state, partially uploaded, and strict mode is on", "UnexpectedUploadingFile");
                            }
                            else
                            {
                                if (options.Dryrun)
                                {
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFile", "Would delete remote file: {0}", i.Name);
                                }
                                else if (verifyMode == VerifyMode.VerifyOnly)
                                {
                                    Logging.Log.WriteInformationMessage(LOGTAG, "WouldDeleteIncompleteFile", "Would remove incomplete remote file listed as {0}: {1}, but defering as {2} is requested", i.State, i.Name, verifyMode);
                                }
                                else
                                {
                                    Logging.Log.WriteInformationMessage(LOGTAG, "RemovingIncompleteFile", "Removing incomplete remote file listed as {0}: {1}", i.State, i.Name);
                                    deleteList.Add(new KeyValuePair<string, long>(i.Name, i.Size));
                                }
                            }
                        }
                        break;

                    case RemoteVolumeState.Uploaded:
                        if (!remoteFound)
                            missing.Add(i);
                        else if (correctSize)
                            updateList.Add(new RemoteVolumeUpdate(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash, false, new TimeSpan(0)));
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
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnknownFileState", null, "Unknown state for remote file listed as {0}: {1}", i.State, i.Name);
                        break;
                }
            }

            // Process all updates after releasing the reader
            foreach (var e in deleteList)
                await backendManager.DeleteAsync(e.Key, e.Value, true, CancellationToken.None).ConfigureAwait(false);
            foreach (var e in unlinkList)
                database.UnlinkRemoteVolume(e.Key, e.Value);
            foreach (var e in updateList)
                database.UpdateRemoteVolume(e.Name, e.State, e.Size, e.Hash, e.SuppressCleanup, e.GraceTime);

            await backendManager.WaitForEmptyAsync(database, CancellationToken.None).ConfigureAwait(false);

            // Batch cleanup deleted volumes in DB
            if (verifyMode == VerifyMode.VerifyStrict && missingUploadingVolumes.Count > 0)
                throw new RemoteListVerificationException($"The remote volumes {string.Join(", ", missingUploadingVolumes)} are supposed to be deleted, but strict mode is on. Try running the \"repair\" command", "DeleteDuringStrictMode");
            else if (verifyMode == VerifyMode.VerifyAndClean || verifyMode == VerifyMode.VerifyStrict)
            {
                database.RemoveRemoteVolumes(missingUploadingVolumes.Concat(temporaryAndDeletedVolumes));
                // Clear the flag after we have cleaned up
                if (!options.Dryrun)
                    database.TerminatedWithActiveUploads = false;
            }
            else if (verifyMode == VerifyMode.VerifyOnly && database.TerminatedWithActiveUploads)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ActiveUploadsDetected", null, "Active uploads detected, but no cleanup was performed. Run the \"repair\" command to clean up the incomplete files");
            }

            foreach (var i in missingHash)
                Logging.Log.WriteWarningMessage(LOGTAG, "MissingRemoteHash", null, "Remote file {1} is listed as {0} with size {2} but should be {3}, please verify the sha256 hash \"{4}\"", i.Item2.State, i.Item2.Name, i.Item1, i.Item2.Size, i.Item2.Hash);

            return new RemoteAnalysisResult()
            {
                ParsedVolumes = remotelist,
                OtherVolumes = otherlist,
                ExtraVolumes = lookup.Values,
                MissingVolumes = missing,
                VerificationRequiredVolumes = missingHash.Select(x => x.Item2)
            };
        }

        private static async Task CheckQuota(IBackendManager backendManager, Options options, IBackendWriter log, long knownFileSize)
        {
            if (options.QuotaDisable)
                return;

            var quota = await backendManager.GetQuotaInfoAsync(CancellationToken.None).ConfigureAwait(false);
            if (quota != null)
            {
                log.TotalQuotaSpace = quota.TotalQuotaSpace;
                log.FreeQuotaSpace = quota.FreeQuotaSpace;

                // Check to see if there should be a warning or error about the quota
                // Since this processor may be called multiple times during a backup
                // (both at the start and end, for example), the log keeps track of
                // whether a quota error or warning has been sent already.
                // Note that an error can still be sent later even if a warning was sent earlier.
                if (!log.ReportedQuotaError && quota.FreeQuotaSpace == 0)
                {
                    log.ReportedQuotaError = true;
                    Logging.Log.WriteErrorMessage(LOGTAG, "BackendQuotaExceeded", null, "Backend quota has been exceeded: Using {0} of {1} ({2} available)", Library.Utility.Utility.FormatSizeString(knownFileSize), Library.Utility.Utility.FormatSizeString(quota.TotalQuotaSpace), Library.Utility.Utility.FormatSizeString(quota.FreeQuotaSpace));
                }
                else if (!log.ReportedQuotaWarning && !log.ReportedQuotaError && quota.FreeQuotaSpace >= 0) // Negative value means the backend didn't return the quota info
                {
                    // Warnings are sent if the available free space is less than the given percentage of the total backup size.
                    double warningThreshold = options.QuotaWarningThreshold / (double)100;
                    if (quota.FreeQuotaSpace < warningThreshold * knownFileSize)
                    {
                        log.ReportedQuotaWarning = true;
                        Logging.Log.WriteWarningMessage(LOGTAG, "BackendQuotaNear", null, "Backend quota is close to being exceeded: Using {0} of {1} ({2} available)", Library.Utility.Utility.FormatSizeString(knownFileSize), Library.Utility.Utility.FormatSizeString(quota.TotalQuotaSpace), Library.Utility.Utility.FormatSizeString(quota.FreeQuotaSpace));
                    }
                }
            }

            log.AssignedQuotaSpace = options.QuotaSize;
            if (log.AssignedQuotaSpace != -1)
            {
                // Check assigned quota
                if (!log.ReportedQuotaError && knownFileSize > log.AssignedQuotaSpace)
                {
                    log.ReportedQuotaError = true;
                    Logging.Log.WriteErrorMessage(LOGTAG, "AssignedQuotaExceeded", null, "Assigned quota has been exceeded: Using {0} of {1}", Library.Utility.Utility.FormatSizeString(knownFileSize), Library.Utility.Utility.FormatSizeString(log.AssignedQuotaSpace));
                }
                else if (!log.ReportedQuotaWarning && !log.ReportedQuotaError)
                {
                    // Warnings are sent if the available free space is less than the given percentage of the total backup size.
                    double warningThreshold = options.QuotaWarningThreshold / (double)100;
                    long freeSpace = log.AssignedQuotaSpace - knownFileSize;
                    if (freeSpace < warningThreshold * knownFileSize)
                    {
                        log.ReportedQuotaWarning = true;
                        Logging.Log.WriteWarningMessage(LOGTAG, "AssignedQuotaNear", null, "Assigned quota is close to being exceeded: Using {0} of {1}", Library.Utility.Utility.FormatSizeString(knownFileSize), Library.Utility.Utility.FormatSizeString(log.AssignedQuotaSpace));
                    }
                }
            }
        }
    }
}
