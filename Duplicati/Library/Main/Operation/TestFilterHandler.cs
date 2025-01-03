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
using System.IO;
using Duplicati.Library.Snapshots;
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
            var sourcefilter = new Library.Utility.FilterExpression(sources, true);
            var stopToken = m_result.TaskControl.ProgressToken;

            using (var snapshot = BackupHandler.GetSnapshot(sources, m_options))
            {
                Backup.Channels channels = new();
                var source = Backup.FileEnumerationProcess.Run(channels, sources, snapshot, null,
                    m_options.FileAttributeFilter, sourcefilter, filter, m_options.SymlinkPolicy,
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
                            var path = await self.source.ReadAsync();
                            var fa = FileAttributes.Normal;
                            try { fa = snapshot.GetAttributes(path); }
                            catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", path, ex.Message); }

                            // Analyze symlinks
                            var isSymlink = snapshot.IsSymlink(path, fa);
                            string symlinkTarget = null;

                            if (isSymlink)
                                try { symlinkTarget = snapshot.GetSymlinkTarget(path); }
                                catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "SymlinkTargetReadFailure", ex, "Failed to read symlink target for path: {0}", path); }

                            if (isSymlink && m_options.SymlinkPolicy == Options.SymlinkStrategy.Store && !string.IsNullOrWhiteSpace(symlinkTarget))
                            {
                                // Skip stored symlinks
                                continue;
                            }

                            // Go for the symlink target, as we know we follow symlinks
                            if (!string.IsNullOrWhiteSpace(symlinkTarget))
                            {
                                path = symlinkTarget;
                                fa = FileAttributes.Normal;
                                try { fa = snapshot.GetAttributes(path); }
                                catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", path, ex.Message); }
                            }

                            // Proceed with non-folders
                            if (!((fa & FileAttributes.Directory) == FileAttributes.Directory))
                            {
                                m_result.FileCount++;
                                var size = -1L;

                                try
                                {
                                    size = snapshot.GetFileSize(path);
                                    m_result.FileSize += size;
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "SizeReadFailed", "Failed to read length of file {0}: {1}", path, ex.Message);
                                }


                                if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || size < m_options.SkipFilesLargerThan)
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "IncludeFile", "Including file: {0} ({1})", path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
                                else
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ExcludeLargeFile", "Excluding file due to size: {0} ({1})", path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
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

