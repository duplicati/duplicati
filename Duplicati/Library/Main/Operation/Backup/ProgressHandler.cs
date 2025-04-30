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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The progress handler reads all progress messages and reports progress as if the processing is sequential
    /// </summary>
    internal static class ProgressHandler
    {
        public static Task Run(Channels channels, BackupResults stat)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = channels.ProgressEvents.AsRead()
            },

            async self =>
            {
                var filesStarted = new Dictionary<string, long>();
                var fileProgress = new Dictionary<string, long>();
                long processedFileCount = 0;
                long processedFileSize = 0;
                string current = null;

                while(true)
                {
                    var t = await self.Input.ReadAsync();
                    switch(t.Type)
                    {
                        case EventType.FileStarted:
                            filesStarted[t.Filepath] = t.Length;
                            fileProgress[t.Filepath] = 0;
                            break;
                        case EventType.FileProgressUpdate:
                            if (t.Filepath == current)
                                stat.OperationProgressUpdater.UpdateFileProgress(t.Length);
                            break;
                        case EventType.FileClosed:
                            if (fileProgress.ContainsKey(t.Filepath))
                                fileProgress[t.Filepath] = t.Length;

                            if (t.Filepath == current)
                            {
                                stat.OperationProgressUpdater.UpdateFileProgress(t.Length);
                                current = null;
                            }

                            processedFileCount += 1;
                            processedFileSize += t.Length;

                            stat.OperationProgressUpdater.UpdatefilesProcessed(processedFileCount, processedFileSize);
                            filesStarted.Remove(t.Filepath);
                            fileProgress.Remove(t.Filepath);
                            break;
                        case EventType.FileSkipped:

                            processedFileCount += 1;
                            processedFileSize += t.Length;

                            stat.OperationProgressUpdater.UpdatefilesProcessed(processedFileCount, processedFileSize);
                            break;
                    }

                    if (current == null)
                    {
                        current = filesStarted.OrderByDescending(x => x.Value).Select(x => x.Key).FirstOrDefault();
                        if (current != null)
                        {
                            stat.OperationProgressUpdater.StartFile(current, filesStarted[current]);
                            if (fileProgress.ContainsKey(current) && fileProgress[current] > 0)
                                stat.OperationProgressUpdater.UpdateFileProgress(fileProgress[current]);
                        }
                    }
                }
            });

        }


    }
}

