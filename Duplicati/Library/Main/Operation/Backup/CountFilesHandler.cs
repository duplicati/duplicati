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

using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots.USN;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class CountFilesHandler
    {
        public static async Task Run(
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
            // Keep the log channel from the parent scope
            using (Logging.Log.StartIsolatingScope(true))
            {
                Channels channels = new();
                var enumeratorTask = FileEnumerationProcess.Run(
                    channels, sources, journalService, options.FileAttributeFilter, filter,
                    options.SymlinkPolicy, options.HardlinkPolicy,
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
                        while (await taskreader.ProgressRendevouz() && !token.IsCancellationRequested)
                        {
                            var entry = await self.Input.ReadAsync();
                            if (entry.IsFolder)
                                continue;

                            count++;

                            try
                            {
                                size += entry.Size;
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

