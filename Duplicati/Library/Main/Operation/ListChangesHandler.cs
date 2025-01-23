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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class ListChangesHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ListChangesHandler));

        private readonly Options m_options;
        private readonly ListChangesResults m_result;

        public ListChangesHandler(Options options, ListChangesResults result)
        {
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

        public void Run(string baseVersion, string compareVersion, IBackendManager backendManager, IEnumerable<string> filterstrings, IFilter compositefilter, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>> callback)
        {
            var cancellationToken = CancellationToken.None;
            var filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(filterstrings), compositefilter);

            var useLocalDb = !m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath);
            baseVersion = string.IsNullOrEmpty(baseVersion) ? "1" : baseVersion;
            compareVersion = string.IsNullOrEmpty(compareVersion) ? "0" : compareVersion;

            long baseVersionIndex;
            long compareVersionIndex;

            DateTime baseVersionTime;
            DateTime compareVersionTime;

            using (var tmpdb = useLocalDb ? null : new Library.Utility.TempFile())
            using (var db = new Database.LocalListChangesDatabase(useLocalDb ? m_options.Dbpath : (string)tmpdb))
            using (var storageKeeper = db.CreateStorageHelper())
            {
                m_result.SetDatabase(db);

                if (useLocalDb)
                {
                    var dbtimes = db.FilesetTimes.ToList();
                    if (dbtimes.Count < 2)
                        throw new UserInformationException(string.Format("Need at least two backups to show differences, database contains {0} backups", dbtimes.Count), "NeedTwoBackupsToStartDiff");

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
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoLocalDatabase", "No local database, accessing remote store");

                    var parsedlist = (from n in backendManager.ListAsync(cancellationToken).Await()
                                      let p = Volumes.VolumeBase.ParseFilename(n)
                                      where p != null && p.FileType == RemoteVolumeType.Files
                                      orderby p.Time descending
                                      select p).ToArray();

                    var numberedList = parsedlist.Zip(Enumerable.Range(0, parsedlist.Length), (a, b) => new Tuple<long, DateTime, Volumes.IParsedVolume>(b, a.Time, a)).ToList();
                    if (numberedList.Count < 2)
                        throw new UserInformationException(string.Format("Need at least two backups to show differences, database contains {0} backups", numberedList.Count), "NeedTwoBackupsToStartDiff");

                    Volumes.IParsedVolume baseFile;
                    Volumes.IParsedVolume compareFile;

                    var bt = SelectTime(baseVersion, numberedList, out baseVersionIndex, out baseVersionTime, out baseFile);
                    numberedList.Remove(bt);
                    SelectTime(compareVersion, numberedList, out compareVersionIndex, out compareVersionTime, out compareFile);

                    Func<FilelistEntryType, ListChangesElementType> conv = (x) =>
                    {
                        switch (x)
                        {
                            case FilelistEntryType.File:
                                return ListChangesElementType.File;
                            case FilelistEntryType.Folder:
                                return ListChangesElementType.Folder;
                            case FilelistEntryType.Symlink:
                                return ListChangesElementType.Symlink;
                            default:
                                return (ListChangesElementType)(-1);
                        }
                    };

                    if (!m_result.TaskControl.ProgressRendevouz().Await())
                        return;

                    using (var tmpfile = backendManager.GetAsync(baseFile.File.Name, null, baseFile.File.Size, cancellationToken).Await())
                    using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(baseFile.File.Name), tmpfile, m_options))
                        foreach (var f in rd.Files)
                            if (FilterExpression.Matches(filter, f.Path))
                                storageKeeper.AddElement(f.Path, f.Hash, f.Metahash, f.Size, conv(f.Type), false);

                    if (!m_result.TaskControl.ProgressRendevouz().Await())
                        return;

                    using (var tmpfile = backendManager.GetAsync(compareFile.File.Name, null, compareFile.File.Size, cancellationToken).Await())
                    using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(compareFile.File.Name), tmpfile, m_options))
                        foreach (var f in rd.Files)
                            if (FilterExpression.Matches(filter, f.Path))
                                storageKeeper.AddElement(f.Path, f.Hash, f.Metahash, f.Size, conv(f.Type), true);
                }

                var changes = storageKeeper.CreateChangeCountReport();
                var sizes = storageKeeper.CreateChangeSizeReport();

                var lst = (m_options.FullResult || callback != null) ?
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

