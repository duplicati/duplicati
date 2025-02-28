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

#nullable enable

using System;
using System.IO;
using CoCoL;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class TestFilterHandler : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<TestFilterHandler>();

        private readonly Options m_options;
        private readonly TestFilterResults m_result;

        public TestFilterHandler(Options options, TestFilterResults results)
        {
            m_options = options;
            m_result = results;
        }

        public async Task RunAsync(string[] sources, Library.Utility.IFilter filter)
        {
            var stopToken = m_result.TaskControl.ProgressToken;

            using (var provider = await BackupHandler.GetSourceProvider(sources, m_options, stopToken).ConfigureAwait(false))
            {
                Backup.Channels channels = new();
                var source = Backup.FileEnumerationProcess.Run(channels, provider, null,
                    m_options.FileAttributeFilter, filter, m_options.SymlinkPolicy,
                    m_options.HardlinkPolicy, m_options.ExcludeEmptyFolders, m_options.IgnoreFilenames,
                    BackupHandler.GetBlacklistedPaths(m_options), null, m_result.TaskControl, null, stopToken);

                var sink = CoCoL.AutomationExtensions.RunTask(new
                {
                    source = channels.SourcePaths.AsRead()
                },
                    async self =>
                    {
                        while (await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            var entry = await self.source.ReadAsync();
                            var fa = entry.IsFolder
                                ? FileAttributes.Directory
                                : FileAttributes.Normal;

                            try { fa = entry.Attributes; }
                            catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", entry.Path, ex.Message); }

                            // Analyze symlinks
                            string? symlinkTarget = null;
                            try { symlinkTarget = entry.SymlinkTarget; }
                            catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "SymlinkTargetReadFailure", ex, "Failed to read symlink target for path: {0}", entry.Path); }

                            if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store && !string.IsNullOrWhiteSpace(symlinkTarget))
                            {
                                // Skip stored symlinks
                                continue;
                            }

                            // Go for the symlink target, as we know we follow symlinks
                            if (!string.IsNullOrWhiteSpace(symlinkTarget))
                            {
                                var targetEntry = await provider.GetEntry(symlinkTarget, false, stopToken).ConfigureAwait(false);
                                fa = FileAttributes.Normal;

                                try { fa = targetEntry!.Attributes; }
                                catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", entry.Path, ex.Message); }

                                // If we guessed wrong and the symlink target is a folder, we need to fetch it with the correct flag
                                if (fa.HasFlag(FileAttributes.Directory))
                                    targetEntry = await provider.GetEntry(symlinkTarget, true, stopToken).ConfigureAwait(false);

                                // No such target
                                if (targetEntry == null)
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "SymlinkTargetNotFound", "Symlink target not found: {0}", symlinkTarget);
                                    continue;
                                }
                            }

                            // Proceed with non-folders
                            if (!entry.IsFolder)
                            {
                                m_result.FileCount++;
                                var size = -1L;

                                try
                                {
                                    size = entry.Size;
                                    m_result.FileSize += size;
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "SizeReadFailed", "Failed to read length of file {0}: {1}", entry.Path, ex.Message);
                                }

                                if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || size < m_options.SkipFilesLargerThan)
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "IncludeFile", "Including file: {0} ({1})", entry.Path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
                                else
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ExcludeLargeFile", "Excluding file due to size: {0} ({1})", entry.Path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
                            }
                        }
                    }
                );

                await Task.WhenAll(source, sink).ConfigureAwait(false);
            }
        }

        #region IDisposable implementation
        public void Dispose()
        {
        }
        #endregion
    }
}

