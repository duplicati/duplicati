//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class ListBrokenFilesHandler
    {
        protected string m_backendurl;
        protected Options m_options;
        protected ListBrokenFilesResults m_result;

        public ListBrokenFilesHandler(string backend, Options options, ListBrokenFilesResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public void Run(Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler = null)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            using (var db = new Database.LocalListBrokenFilesDatabase(m_options.Dbpath))
            using (var tr = db.BeginTransaction())
                DoRun(db, tr, filter, callbackhandler);
        }

        public static Tuple<DateTime, long, long>[] GetBrokenFilesetsFromRemote(string backendurl, BasicResults result, Database.LocalListBrokenFilesDatabase db, System.Data.IDbTransaction transaction, Options options, out List<Database.RemoteVolumeEntry> missing)
        {
            missing = null;
            var brokensets = db.GetBrokenFilesets(options.Time, options.Version, transaction).ToArray();

            if (brokensets.Length == 0)
            {
                if (db.RepairInProgress)
                    throw new UserInformationException("Cannot continue because the database is marked as being under repair, but does not have broken files.");

                result.AddMessage("No broken filesets found in database, checking for missing remote files");

                using (var backend = new BackendManager(backendurl, options, result.BackendWriter, db))
                {
                    var remotestate = FilelistProcessor.RemoteListAnalysis(backend, options, db, result.BackendWriter, null);
                    if (!remotestate.ParsedVolumes.Any())
                        throw new UserInformationException("No remote volumes were found, refusing purge");

                    missing = remotestate.MissingVolumes.ToList();
                    if (missing.Count == 0)
                    {
                        result.AddMessage("Skipping operation because no files were found to be missing, and no filesets were recorded as broken.");
                        return null;
                    }

                    // Mark all volumes as disposable
                    foreach (var f in missing)
                        db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash, transaction);

                    result.AddMessage(string.Format("Marked {0} remote files for deletion", missing.Count));

                    // Drop all content from tables
                    db.RemoveMissingBlocks(missing.Select(x => x.Name), transaction);
                }
                brokensets = db.GetBrokenFilesets(options.Time, options.Version, transaction).ToArray();
            }

            return brokensets;
        }

        private void DoRun(Database.LocalListBrokenFilesDatabase db, System.Data.IDbTransaction transaction, Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler)
        {
            if (filter != null && !filter.Empty)
                throw new UserInformationException("Filters are not supported for this operation");

            if (db.PartiallyRecreated)
                throw new UserInformationException("The command does not work on partially recreated databases");

            List<Database.RemoteVolumeEntry> missing;
            var brokensets = GetBrokenFilesetsFromRemote(m_backendurl, m_result, db, transaction, m_options, out missing);
            if (brokensets == null)
                return;

            if (brokensets.Length == 0)
            {
                m_result.BrokenFiles = new Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>[0];
                m_result.AddMessage("No broken filesets found");
                return;
            }

            var fstimes = db.FilesetTimes.ToList();

            var brokenfilesets = 
                brokensets.Select(x => new 
                {
                    Version = fstimes.FindIndex(y => y.Key == x.Item2),
                    Timestamp = x.Item1,
                    FilesetID = x.Item2,
                    BrokenCount = x.Item3
                }
              )
              .ToArray();


            m_result.BrokenFiles =
                brokenfilesets.Select(
                    x => new Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>(
                                x.Version, 
                                x.Timestamp, 
                                callbackhandler == null && !m_options.ListSetsOnly
                                    ? db.GetBrokenFilenames(x.FilesetID, transaction).ToArray().AsEnumerable()
                                    : new MockList<Tuple<string, long>>((int)x.BrokenCount)
                    ))
                .ToArray();


            if (callbackhandler != null)
                foreach (var bs in brokenfilesets)
                    foreach (var fe in db.GetBrokenFilenames(bs.FilesetID, transaction))
                        if (!callbackhandler(bs.Version, bs.Timestamp, bs.BrokenCount, fe.Item1, fe.Item2))
                            break;
        }

        private class MockList<T> : IList<T>
        {
            public MockList(int count)
            {
                Count = count;
            }

            public T this[int index]
            {
                get { throw new NotImplementedException(); }
                set { throw new NotImplementedException(); }
            }

            public int Count { get; private set; }

            public bool IsReadOnly { get { return true; }}

            public void Add(T item)
            {
                throw new NotImplementedException();
            }

            public void Clear()
            {
                throw new NotImplementedException();
            }

            public bool Contains(T item)
            {
                throw new NotImplementedException();
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                throw new NotImplementedException();
            }

            public IEnumerator<T> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            public int IndexOf(T item)
            {
                throw new NotImplementedException();
            }

            public void Insert(int index, T item)
            {
                throw new NotImplementedException();
            }

            public bool Remove(T item)
            {
                throw new NotImplementedException();
            }

            public void RemoveAt(int index)
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new NotImplementedException();
            }
        }
    }
}
