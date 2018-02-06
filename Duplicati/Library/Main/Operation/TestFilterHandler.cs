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
using System.IO;
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
            var storeSymlinks = m_options.SymlinkPolicy == Duplicati.Library.Main.Options.SymlinkStrategy.Store;
            var sourcefilter = new Library.Utility.FilterExpression(sources, true);

            using(var snapshot = BackupHandler.GetSnapshot(sources, m_options, m_result))
            {
                foreach(var path in new BackupHandler.FilterHandler(snapshot, m_options.FileAttributeFilter, sourcefilter, filter, m_options.SymlinkPolicy, m_options.HardlinkPolicy, m_result).EnumerateFilesAndFolders())
                {
                    var fa = FileAttributes.Normal;
                    try { fa = snapshot.GetAttributes(path); }
                    catch { }
                    
                    if (storeSymlinks && snapshot.IsSymlink(path, fa))
                    {
                        m_result.AddVerboseMessage("Storing symlink: {0}", path);
                    }
                    else if ((fa & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        m_result.AddVerboseMessage("Including folder: {0}", path);
                    }
                    else
                    {
                        m_result.FileCount++;
                        var size = -1L;
                    
                        try
                        {
                            size = snapshot.GetFileSize(path);
                            m_result.FileSize += size;
                        }
                        catch
                        {
                        }
                    
                    
                        if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || size < m_options.SkipFilesLargerThan)
                            m_result.AddVerboseMessage("Including file: {0} ({1})", path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
                        else
                            m_result.AddVerboseMessage("Excluding file due to size: {0} ({1})", path, size < 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));                    
                    }
                    
                }
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

