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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class ListBrokenFilesHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ListBrokenFilesHandler));
        protected readonly Options m_options;
        protected readonly ListBrokenFilesResults m_result;

        public ListBrokenFilesHandler(Options options, ListBrokenFilesResults result)
        {
            m_options = options;
            m_result = result;
        }

        public void Run(IBackendManager backendManager, IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler = null)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            using (var db = new Database.LocalListBrokenFilesDatabase(m_options.Dbpath))
            using (var tr = db.BeginTransaction())
                DoRun(backendManager, db, tr, filter, callbackhandler);
        }

        public static Tuple<DateTime, long, long>[] GetBrokenFilesetsFromRemote(IBackendManager backendManager, BasicResults result, Database.LocalListBrokenFilesDatabase db, System.Data.IDbTransaction transaction, Options options, out List<Database.RemoteVolumeEntry> missing)
        {
            missing = null;
            var brokensets = db.GetBrokenFilesets(options.Time, options.Version, transaction).ToArray();

            if (brokensets.Length == 0)
            {
                if (db.RepairInProgress)
                    throw new UserInformationException("Cannot continue because the database is marked as being under repair, but does not have broken files.", "CannotListOnDatabaseInRepair");

                Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenFilesetsInDatabase", "No broken filesets found in database, checking for missing remote files");

                var remotestate = FilelistProcessor.RemoteListAnalysis(backendManager, options, db, result.BackendWriter, null).Await();
                if (!remotestate.ParsedVolumes.Any())
                    throw new UserInformationException("No remote volumes were found, refusing purge", "CannotPurgeWithNoRemoteVolumes");

                missing = remotestate.MissingVolumes.ToList();
                if (missing.Count == 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoMissingFilesFound", "Skipping operation because no files were found to be missing, and no filesets were recorded as broken.");
                    return null;
                }

                // Mark all volumes as disposable
                foreach (var f in missing)
                    db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash, transaction);

                Logging.Log.WriteInformationMessage(LOGTAG, "MarkedRemoteFilesForDeletion", "Marked {0} remote files for deletion", missing.Count);

                // Drop all content from tables
                db.RemoveMissingBlocks(missing.Select(x => x.Name), transaction);
                brokensets = db.GetBrokenFilesets(options.Time, options.Version, transaction).ToArray();
            }

            return brokensets;
        }

        private void DoRun(IBackendManager backendManager, Database.LocalListBrokenFilesDatabase db, System.Data.IDbTransaction transaction, Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler)
        {
            if (filter != null && !filter.Empty)
                throw new UserInformationException("Filters are not supported for this operation", "FiltersAreNotSupportedForListBrokenFiles");

            if (db.PartiallyRecreated)
                throw new UserInformationException("The command does not work on partially recreated databases", "ListBrokenFilesDoesNotWorkOnPartialDatabase");

            List<Database.RemoteVolumeEntry> missing;
            var brokensets = GetBrokenFilesetsFromRemote(backendManager, m_result, db, transaction, m_options, out missing);
            if (brokensets == null)
                return;

            if (brokensets.Length == 0)
            {
                m_result.BrokenFiles = new Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>[0];

                if (missing == null)
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenFilesets", "Found no broken filesets");
                else if (missing.Count == 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenFilesetsOrMissingFiles", "Found no broken filesets and no missing remote files");
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenSetsButMissingRemoteFiles", string.Format("Found no broken filesets, but {0} missing remote files. Run purge-broken-files.", missing.Count));

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

            public bool IsReadOnly => true;

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
