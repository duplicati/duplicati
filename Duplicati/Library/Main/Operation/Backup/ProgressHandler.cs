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
        public static Task Run(BackupResults stat)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Channels.ProgressEvents.ForRead
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
                            processedFileSize += 1;

                            stat.OperationProgressUpdater.UpdatefilesProcessed(processedFileCount, processedFileSize);
                            filesStarted.Remove(t.Filepath);
                            fileProgress.Remove(t.Filepath);
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

