#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion
using System;
using System.IO;
using System.Threading;
using Duplicati.Library.Snapshots;
using CoCoL;

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

        public void Run(string[] sources, Library.Utility.IFilter filter, CancellationToken token)
        {
            var sourcefilter = new Library.Utility.FilterExpression(sources, true);

            using (var snapshot = BackupHandler.GetSnapshot(sources, m_options))
            using (new IsolatedChannelScope())
            {
                var source = Operation.Backup.FileEnumerationProcess.Run(sources, snapshot, null,
                    m_options.FileAttributeFilter, sourcefilter, filter, m_options.SymlinkPolicy,
                    m_options.HardlinkPolicy, m_options.ExcludeEmptyFolders, m_options.IgnoreFilenames, null,
                    m_result.TaskReader, token);

                var sink = CoCoL.AutomationExtensions.RunTask(
                    new { source = Operation.Backup.Channels.SourcePaths.ForRead },
                    async self =>
                    {
                        while (true)
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

                System.Threading.Tasks.Task.WhenAll(source, sink).WaitForTaskOrThrow();
            }
        }

        #region IDisposable implementation
        public void Dispose()
        {
        }
        #endregion
    }
}

