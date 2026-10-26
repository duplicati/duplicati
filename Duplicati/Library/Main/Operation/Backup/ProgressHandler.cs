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

using System;
using CoCoL;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The progress handler reads all progress messages and reports progress as if the processing is sequential.
    /// The files processed in parallel are also reported as a list, for clients that can show them.
    /// </summary>
    internal static class ProgressHandler
    {
        /// <summary>
        /// The maximum number of active files reported
        /// </summary>
        private const int MAX_ACTIVE_FILES = 32;

        public static Task RunAsync(Channels channels, BackupResults stat)
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

                // Bytes actually read, which unlike the processed size leaves out skipped files.
                // The bytes of open files are counted from their last progress update.
                long closedBytesRead = 0;
                long openBytesRead = 0;
                stat.OperationProgressUpdater.UpdateSourceBytesRead(0);

                // The list is read from the thread that reports progress, so changes to the files are locked
                var filesLock = new object();
                stat.OperationProgressUpdater.SetActiveFilesProvider(() =>
                {
                    lock (filesLock)
                        return filesStarted
                            .OrderByDescending(x => x.Value)
                            .Take(MAX_ACTIVE_FILES)
                            .Select(x => new ActiveFileProgress(x.Key, x.Value, fileProgress.TryGetValue(x.Key, out var offset) ? offset : 0))
                            .ToArray();
                });

                while (true)
                {
                    var t = await self.Input.ReadAsync();
                    lock (filesLock)
                    {
                        switch (t.Type)
                        {
                            case EventType.FileStarted:
                                filesStarted[t.Filepath] = t.Length;
                                fileProgress[t.Filepath] = 0;
                                break;
                            case EventType.FileProgressUpdate:
                                if (fileProgress.TryGetValue(t.Filepath, out var lastOffset))
                                {
                                    openBytesRead += t.Length - lastOffset;
                                    fileProgress[t.Filepath] = t.Length;
                                    stat.OperationProgressUpdater.UpdateSourceBytesRead(closedBytesRead + openBytesRead);
                                }
                                if (t.Filepath == current)
                                    stat.OperationProgressUpdater.UpdateFileProgress(t.Length);
                                break;
                            case EventType.FileClosed:
                                if (fileProgress.TryGetValue(t.Filepath, out var openOffset))
                                {
                                    openBytesRead -= openOffset;
                                    fileProgress[t.Filepath] = t.Length;
                                }
                                closedBytesRead += t.Length;
                                stat.OperationProgressUpdater.UpdateSourceBytesRead(closedBytesRead + openBytesRead);

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

