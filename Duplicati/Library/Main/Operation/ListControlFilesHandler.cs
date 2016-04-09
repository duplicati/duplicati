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
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation
{
    internal class ListControlFilesHandler
    {
        private Options m_options;
        private string m_backendurl;
        private ListResults m_result;
        
        public ListControlFilesHandler(string backendurl, Options options, ListResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;
        }

        public void Run(IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null)
        {
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(System.IO.File.Exists(m_options.Dbpath) ? m_options.Dbpath : (string)tmpdb, "ListControlFiles", true))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
                
                var filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(filterstrings), compositefilter);
                
                try
                {
                    var filteredList = ListFilesHandler.ParseAndFilterFilesets(backend.List(), m_options);
                    if (filteredList.Count == 0)
                        throw new Exception("No filesets found on remote target");
    
                    Exception lastEx = new Exception("No suitable files found on remote target");
    
                    foreach(var fileversion in filteredList)
                        try
                        {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                return;
                        
                            var file = fileversion.Value.File;
                            long size;
                            string hash;
                            RemoteVolumeType type;
                            RemoteVolumeState state;
                            if (!db.GetRemoteVolume(file.Name, out hash, out size, out type, out state))
                                size = file.Size;
    
                            var files = new List<Library.Interface.IListResultFile>();
                            using (var tmpfile = backend.Get(file.Name, size, hash))
                            using (var tmp = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(file.Name), tmpfile, m_options))
                                foreach (var cf in tmp.ControlFiles)
                                    if (Library.Utility.FilterExpression.Matches(filter, cf.Key))
                                        files.Add(new ListResultFile(cf.Key, null));
                            
                            m_result.SetResult(new Library.Interface.IListResultFileset[] { new ListResultFileset(fileversion.Key, fileversion.Value.Time, -1, -1) }, files);
                            lastEx = null;
                            break;
                        }
                        catch(Exception ex)
                        {
                            lastEx = ex;
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
    
                    if (lastEx != null)
                        throw lastEx;
                }
                finally
                {
                    backend.WaitForComplete(db, null);
                }
            }
        }    }
}

