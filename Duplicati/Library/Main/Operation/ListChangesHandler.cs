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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class ListChangesHandler
    {
        private string m_backendurl;
        private Options m_options;
        private ListChangesResults m_result;

        public ListChangesHandler(string backend, Options options, ListChangesResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }
        
        private static Tuple<long, DateTime, T> SelectTime<T>(string value, IEnumerable<Tuple<long, DateTime, T>> list, out long index, out DateTime time, out T el)
        {
            long indexValue;
            Tuple<long, DateTime, T> res;
            if (!long.TryParse(value, out indexValue))
            {
                var t = Library.Utility.Timeparser.ParseTimeInterval(value, DateTime.Now, true);
                res = list.OrderBy(x => Math.Abs((x.Item2 - t).Ticks)).First();
            }
            else
            {
                res = list.OrderBy(x => Math.Abs(x.Item1 - indexValue)).First();
            }
            
            index = res.Item1;
            time = res.Item2;
            el = res.Item3;
            return res;
        }

        public void Run(string baseVersion, string compareVersion, IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null, Action<IListChangesResults, IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>>> callback = null)
        {
            var filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(filterstrings), compositefilter);
            
            var useLocalDb = !m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath);
            baseVersion = string.IsNullOrEmpty(baseVersion) ? "1" : baseVersion;
            compareVersion = string.IsNullOrEmpty(compareVersion) ? "0" : compareVersion;
            
            long baseVersionIndex = -1;
            long compareVersionIndex = -1;
            
            DateTime baseVersionTime = new DateTime(0);
            DateTime compareVersionTime = new DateTime(0);
            
            using(var tmpdb = useLocalDb ? null : new Library.Utility.TempFile())
            using(var db = new Database.LocalListChangesDatabase(useLocalDb ? m_options.Dbpath : (string)tmpdb))
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            using(var storageKeeper = db.CreateStorageHelper())
            {
                m_result.SetDatabase(db);
                
                if (useLocalDb)
                {
                    var dbtimes = db.FilesetTimes.ToList();
                    if (dbtimes.Count < 2)
                        throw new UserInformationException(string.Format("Need at least two backups to show differences, database contains {0} backups", dbtimes.Count));
                    
                    long baseVersionId;
                    long compareVersionId;
                    
                    var times = dbtimes.Zip(Enumerable.Range(0, dbtimes.Count), (a, b) => new Tuple<long, DateTime, long>(b, a.Value, a.Key)).ToList();
                    var bt = SelectTime(baseVersion, times, out baseVersionIndex, out baseVersionTime, out baseVersionId);
                    times.Remove(bt);
                    SelectTime(compareVersion, times, out compareVersionIndex, out compareVersionTime, out compareVersionId);
                                            
                    storageKeeper.AddFromDb(baseVersionId, false, filter);
                    storageKeeper.AddFromDb(compareVersionId, true, filter);
                }
                else
                {
                    m_result.AddMessage("No local database, accessing remote store");
                    
                    var parsedlist = (from n in backend.List()
                                let p = Volumes.VolumeBase.ParseFilename(n)
                                where p != null && p.FileType == RemoteVolumeType.Files
                                orderby p.Time descending
                                select p).ToArray();
                                
                    var numberedList = parsedlist.Zip(Enumerable.Range(0, parsedlist.Length), (a, b) => new Tuple<long, DateTime, Volumes.IParsedVolume>(b, a.Time, a)).ToList();
                    if (numberedList.Count < 2)
                        throw new UserInformationException(string.Format("Need at least two backups to show differences, database contains {0} backups", numberedList.Count));

                    Volumes.IParsedVolume baseFile;
                    Volumes.IParsedVolume compareFile;
                    
                    var bt = SelectTime(baseVersion, numberedList, out baseVersionIndex, out baseVersionTime, out baseFile);
                    numberedList.Remove(bt);
                    SelectTime(compareVersion, numberedList, out compareVersionIndex, out compareVersionTime, out compareFile);
                    
                    Func<FilelistEntryType, Library.Interface.ListChangesElementType> conv = (x) => {
                        switch (x)
                        {
                            case FilelistEntryType.File:
                                return Library.Interface.ListChangesElementType.File;
                            case FilelistEntryType.Folder:
                                return Library.Interface.ListChangesElementType.Folder;
                            case FilelistEntryType.Symlink:
                                return Library.Interface.ListChangesElementType.Symlink;
                            default:
                                return (Library.Interface.ListChangesElementType)(-1);
                        }
                    };
                    
                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                        return;
                        
                    using(var tmpfile = backend.Get(baseFile.File.Name, baseFile.File.Size, null))
                    using(var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(baseFile.File.Name), tmpfile, m_options))
                        foreach(var f in rd.Files)
                            if (Library.Utility.FilterExpression.Matches(filter, f.Path))
                                storageKeeper.AddElement(f.Path, f.Hash, f.Metahash, f.Size, conv(f.Type), false);
                                
                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                        return;
                    
                    using(var tmpfile = backend.Get(compareFile.File.Name, compareFile.File.Size, null))
                    using(var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(compareFile.File.Name), tmpfile, m_options))
                        foreach(var f in rd.Files)
                            if (Library.Utility.FilterExpression.Matches(filter, f.Path))
                                storageKeeper.AddElement(f.Path, f.Hash, f.Metahash, f.Size, conv(f.Type), true);
                }
                
                var changes = storageKeeper.CreateChangeCountReport();
                var sizes = storageKeeper.CreateChangeSizeReport();

                var lst = (m_options.Verbose || m_options.FullResult || callback != null) ?
                        (from n in storageKeeper.CreateChangedFileReport()
                         select n) : null;

                m_result.SetResult(
                    baseVersionTime, baseVersionIndex, compareVersionTime, compareVersionIndex,
                    changes.AddedFolders, changes.AddedSymlinks, changes.AddedFiles,
                    changes.DeletedFolders, changes.DeletedSymlinks, changes.DeletedFiles,
                    changes.ModifiedFolders, changes.ModifiedSymlinks, changes.ModifiedFiles,
                    sizes.AddedSize, sizes.DeletedSize, sizes.PreviousSize, sizes.CurrentSize,
                    (lst == null || callback == null) ? null : lst.ToArray()
                );

                if (callback != null)
                    callback(m_result, lst);

                return;                                
            }      
        }
    }
}

