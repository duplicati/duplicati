// Copyright (C) 2026, The Duplicati Team
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots.USN;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class CountFilesHandler
    {
        public static async Task RunAsync(
            ISourceProvider sources,
            UsnJournalService journalService,
            BackupResults result,
            Options options,
            IFilter filter,
            HashSet<string> blacklistPaths,
            Common.ITaskReader taskreader,
            System.Threading.CancellationToken token
        )
        {
            // The backup goes through the same paths and logs the same messages, so the
            // messages of this pass are kept out of the log. The scope must stay current
            // for the enumeration started below, so it is not detached.
            using (Logging.Log.StartIsolatingScope(false))
            {
                Channels channels = new();
                var enumeratorTask = FileEnumerationProcess.RunAsync(
                    channels, sources, journalService, options.FileAttributeFilter, filter,
                    options.SymlinkPolicy, options.HardlinkPolicy, options.DisableBackupExclusionXattr,
                    options.ExcludeEmptyFolders, options.IgnoreFilenames,
                    blacklistPaths, options.ChangedFilelist, taskreader, null, token);

                var counterTask = AutomationExtensions.RunTask(new
                {
                    Input = channels.SourcePaths.AsRead()
                },

                async self =>
                {
                    var count = 0L;
                    var size = 0L;

                    try
                    {
                        while (await taskreader.ProgressRendevouzAsync() && !token.IsCancellationRequested)
                        {
                            var entry = await self.Input.ReadAsync();
                            if (entry.IsFolder)
                                continue;

                            count++;

                            try
                            {
                                // Reading the size is a synchronous call that can get stuck on a source
                                // that stopped answering, so it is read on its own and not waited for
                                // once the counting is stopped
                                var entrySize = await Task.Run(() => entry.Size, CancellationToken.None).UntilCancelledAsync(token).ConfigureAwait(false);
                                if (entrySize >= 0)
                                    size += entrySize;
                            }
                            catch
                            {
                            }

                            result.OperationProgressUpdater.UpdatefileCount(count, size, false);
                        }
                    }
                    finally
                    {
                        result.OperationProgressUpdater.UpdatefileCount(count, size, true);
                    }
                });

                await Task.WhenAll(enumeratorTask, counterTask);
            }
        }
    }
}

