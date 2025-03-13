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
using CoCoL;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class processes paths for metadata and emits the metadata blocks for storage.
    /// Folders and symlinks in the database, and paths are forwarded to be scanned for changes
    /// </summary>
    internal static class MetadataPreProcess
    {
        private static readonly string FILELOGTAG = Logging.Log.LogTagFromType(typeof(MetadataPreProcess)) + ".FileEntry";

        public class FileEntry
        {
            // From input
            public ISourceProviderEntry Entry;

            // Split
            public long PathPrefixID;
            public string Filename;

            // From database
            public long OldId;
            public DateTime OldModified;
            public long LastFileSize;
            public string OldMetaHash;
            public long OldMetaSize;

            // From filedata
            public DateTime LastWrite;
            public FileAttributes Attributes;

            // After processing metadata
            public IMetahash MetaHashAndSize;
            public bool MetadataChanged;
            public bool TimestampChanged;
        }

        public static Task Run(Channels channels, Options options, LocalBackupDatabase database, long lastfilesetid, ITaskReader taskReader)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = channels.SourcePaths.AsRead(),
                StreamBlockChannel = channels.StreamBlock.AsWrite(),
                Output = channels.ProcessedFiles.AsWrite(),
            },

            async self =>
            {
                var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), options);
                var prevprefix = new KeyValuePair<string, long>(null, -1);

                var CHECKFILETIMEONLY = options.CheckFiletimeOnly;
                var DISABLEFILETIMECHECK = options.DisableFiletimeCheck;

                while (true)
                {
                    var entry = await self.Input.ReadAsync();

                    // We ignore the stop signal, but not the pause and terminate
                    await taskReader.ProgressRendevouz().ConfigureAwait(false);

                    var lastwrite = new DateTime(0, DateTimeKind.Utc);
                    var attributes = entry.IsFolder
                        ? FileAttributes.Directory
                        : FileAttributes.Normal;

                    try
                    {
                        lastwrite = entry.LastModificationUtc;
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(FILELOGTAG, "TimestampReadFailed", ex, "Failed to read timestamp on \"{0}\"", entry.Path);
                    }

                    try
                    {
                        attributes = entry.Attributes;
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", entry.Path, ex.Message);
                    }

                    // If we only have metadata, stop here
                    if (await ProcessMetadata(entry, attributes, lastwrite, options, emptymetadata, database, self.StreamBlockChannel).ConfigureAwait(false))
                    {
                        try
                        {
                            var split = Database.LocalDatabase.SplitIntoPrefixAndName(entry.Path);

                            long prefixid;
                            if (string.Equals(prevprefix.Key, split.Key, StringComparison.Ordinal))
                                prefixid = prevprefix.Value;
                            else
                            {
                                using (await database.LockAsync())
                                    prefixid = database.GetOrCreatePathPrefix(split.Key);
                                prevprefix = new KeyValuePair<string, long>(split.Key, prefixid);
                            }

                            if (CHECKFILETIMEONLY || DISABLEFILETIMECHECK)
                            {
                                long id;
                                DateTime oldModified;
                                long lastFileSize;
                                using (await database.LockAsync())
                                    id = database.GetFileLastModified(prefixid, split.Value, lastfilesetid, false, out oldModified, out lastFileSize);

                                await self.Output.WriteAsync(new FileEntry
                                {
                                    OldId = id,
                                    Entry = entry,
                                    PathPrefixID = prefixid,
                                    Filename = split.Value,
                                    Attributes = attributes,
                                    LastWrite = lastwrite,
                                    OldModified = oldModified,
                                    LastFileSize = lastFileSize,
                                    OldMetaHash = null,
                                    OldMetaSize = -1
                                });
                            }
                            else
                            {
                                long id;
                                DateTime oldModified;
                                long lastFileSize;
                                string oldMetahash;
                                long oldMetasize;
                                using (await database.LockAsync())
                                    id = database.GetFileEntry(prefixid, split.Value, lastfilesetid, out oldModified, out lastFileSize, out oldMetahash, out oldMetasize);
                                await self.Output.WriteAsync(new FileEntry
                                {
                                    OldId = id,
                                    Entry = entry,
                                    PathPrefixID = prefixid,
                                    Filename = split.Value,
                                    Attributes = attributes,
                                    LastWrite = lastwrite,
                                    OldModified = oldModified,
                                    LastFileSize = lastFileSize,
                                    OldMetaHash = oldMetahash,
                                    OldMetaSize = oldMetasize
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsRetiredException())
                                continue;

                            Logging.Log.WriteWarningMessage(FILELOGTAG, "ProcessingMetadataFailed", ex,
                                "Failed to process entry, path: {0}", entry.Path);
                        }
                    }
                }

            });

        }

        /// <summary>
        /// Processes the metadata for the given path.
        /// </summary>
        /// <returns><c>True</c> if the path should be submitted to more analysis, <c>false</c> if there is nothing else to do</returns>
        private static async Task<bool> ProcessMetadata(ISourceProviderEntry entry, FileAttributes attributes, DateTime lastwrite, Options options, IMetahash emptymetadata, LocalBackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            if (entry.IsSymlink)
            {
                // Not all reparse points are symlinks.
                // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
                // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
                // If we can't find a symlink target for the current path, we won't treat it as a symlink.
                string symlinkTarget = null;
                try
                {
                    symlinkTarget = entry.SymlinkTarget;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteExplicitMessage(FILELOGTAG, "SymlinkTargetReadFailure", ex, "Failed to read symlink target for path: {0}", entry.Path);
                }

                if (!string.IsNullOrWhiteSpace(symlinkTarget))
                {
                    if (options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "IgnoreSymlink", "Ignoring symlink {0}", entry.Path);
                        return false;
                    }

                    if (options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                    {
                        var metadata = MetadataGenerator.GenerateMetadata(entry, attributes, options);

                        if (!metadata.ContainsKey("CoreSymlinkTarget"))
                            metadata["CoreSymlinkTarget"] = symlinkTarget;

                        var metahash = Utility.WrapMetadata(metadata, options);
                        await AddSymlinkToOutputAsync(entry.Path, DateTime.UtcNow, metahash, database, streamblockchannel).ConfigureAwait(false);

                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "StoreSymlink", "Stored symlink {0}", entry.Path);
                        // Don't process further
                        return false;
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "FollowingEmptySymlink", "Treating empty symlink as regular path {0}", entry.Path);
                }
            }


            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                IMetahash metahash;

                if (!options.SkipMetadata)
                {
                    metahash = Utility.WrapMetadata(MetadataGenerator.GenerateMetadata(entry, attributes, options), options);
                }
                else
                {
                    metahash = emptymetadata;
                }

                Logging.Log.WriteVerboseMessage(FILELOGTAG, "AddDirectory", "Adding directory {0}", entry.Path);
                await AddFolderToOutputAsync(entry.Path, lastwrite, metahash, database, streamblockchannel).ConfigureAwait(false);
                return false;
            }

            // Regular file, keep going
            return true;
        }

        /// <summary>
        /// Adds metadata to output, and returns the metadataset ID
        /// </summary>
        /// <returns>The metadataset ID.</returns>
        /// <param name="path">The path for which metadata is processed.</param>
        /// <param name="meta">The metadata entry.</param>
        /// <param name="database">The database connection.</param>
        /// <param name="dblock">The database lock.</param>
        /// <param name="streamblockchannel">The channel to write streams to.</param>
        internal static async Task<Tuple<bool, long>> AddMetadataToOutputAsync(string path, IMetahash meta, LocalBackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            StreamProcessResult res;
            using (var ms = new MemoryStream(meta.Blob))
                res = await StreamBlock.ProcessStream(streamblockchannel, path, ms, true, CompressionHint.Default);

            bool found;
            long id;
            using (await database.LockAsync())
                found = database.AddMetadataset(res.Streamhash, res.Streamlength, res.Blocksetid, out id);
            return new Tuple<bool, long>(found, id);
        }

        /// <summary>
        /// Adds a file to the output,
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        private static async Task AddFolderToOutputAsync(string filename, DateTime lastModified, IMetahash meta, LocalBackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            var metadataid = await AddMetadataToOutputAsync(filename, meta, database, streamblockchannel).ConfigureAwait(false);
            using (await database.LockAsync())
                database.AddDirectoryEntry(filename, metadataid.Item2, lastModified);
        }

        /// <summary>
        /// Adds a file to the output,
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="database">The database to use</param>
        /// <param name="streamblockchannel">The channel to write blocks to</param>
        /// <param name="meta">The metadata ti record</param>
        private static async Task AddSymlinkToOutputAsync(string filename, DateTime lastModified, IMetahash meta, LocalBackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            var metadataid = await AddMetadataToOutputAsync(filename, meta, database, streamblockchannel).ConfigureAwait(false);
            using (await database.LockAsync())
                database.AddSymlinkEntry(filename, metadataid.Item2, lastModified);
        }

    }
}

