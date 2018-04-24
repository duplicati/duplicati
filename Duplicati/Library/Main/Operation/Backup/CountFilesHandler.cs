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
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using System.Threading;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class CountFilesHandler
    {
        public static Task Run(Snapshots.ISnapshotService snapshot, BackupResults result, Options options, IFilter sourcefilter, IFilter filter, Common.ITaskReader taskreader, System.Threading.CancellationToken token)
        {
            // Make sure we create the enumeration process in a seperate scope,
            // but keep the log channel from the parent scope
            using(Logging.Log.StartIsolatingScope())
            using(new IsolatedChannelScope())
            {
                var enumeratorTask = Backup.FileEnumerationProcess.Run(snapshot, options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy, options.HardlinkPolicy, options.IgnoreFilenames, options.ChangedFilelist, taskreader);

                var counterTask = AutomationExtensions.RunTask(new 
                {
                    Input = Backup.Channels.SourcePaths.ForRead
                },
                    
                async self =>
                {
                    var count = 0L;
                    var size = 0L;

                    try
                    {
                        while (await taskreader.ProgressAsync && !token.IsCancellationRequested)
                        {
                            var path = await self.Input.ReadAsync();

                            count++;

                            try
                            {
                                size += snapshot.GetFileSize(path);
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

                return Task.WhenAll(enumeratorTask, counterTask);
            }
        }
    }
}

