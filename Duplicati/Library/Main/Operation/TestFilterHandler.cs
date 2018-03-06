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
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Main.Operation
{
    internal class TestFilterHandler : IDisposable
    {
        private readonly Options m_options;
        private TestFilterResults m_result;
        
        public TestFilterHandler(Options options, TestFilterResults results)
        {
            m_options = options;
            m_result = results;
        }
        
        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            var sourcefilter = new Library.Utility.FilterExpression(sources, true);

            using(new IsolatedChannelScope(Common.Channels.LogChannel))
            using(var snapshot = BackupHandler.GetSnapshot(sources, m_options, m_result))
            {
                var enumeratorTask = Backup.FileEnumerationProcess.Run(snapshot, m_options.FileAttributeFilter, sourcefilter, filter, m_options.SymlinkPolicy, m_options.HardlinkPolicy, m_options.ChangedFilelist, m_result.TaskReader);
                var metadataTask = Backup.MetadataPreProcess.RunOnlyRecord(snapshot, m_options);

                var fileCounterTask = AutomationExtensions.RunTask(
                    new
                    {
                        Input = Backup.Channels.ProcessedFiles.ForRead
                    },
                    async self =>
                    {
                        while (await m_result.TaskReader.ProgressAsync)
                        {
                            var f = await self.Input.ReadAsync(CoCoL.Timeout.Infinite);
                            long filestatsize = -1;
                            try
                            {
                                filestatsize = snapshot.GetFileSize(f.Path);
                            }
                            catch
                            {
                            }

                            var tooLargeFile = m_options.SkipFilesLargerThan != long.MaxValue && m_options.SkipFilesLargerThan != 0 && filestatsize >= 0 && filestatsize > m_options.SkipFilesLargerThan;

                            if (tooLargeFile)
                                m_result.AddVerboseMessage("Excluding file due to size: {0} ({1})", f.Path, filestatsize < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(filestatsize));                    
                            else
                                m_result.AddVerboseMessage("Including file: {0} ({1})", f.Path, filestatsize < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(filestatsize));
                        }
                    }
                );

                Task.WhenAll(
                    enumeratorTask,
                    metadataTask,
                    fileCounterTask
                )
                .WaitForTaskOrThrow();
            }
        }


        
        #region IDisposable implementation
        public void Dispose()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}

