//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.MirrorTool
{
    public struct EnumEntry
    {
        public string Path;
        public long LocalTimestamp;
        public long RemoteTimestamp;
        public long LocalRecordedTimestamp;
        public long RemoteRecordedTimestamp;
    }

    public class ListSourceMerger : IEnumerable<EnumEntry>, IDisposable
    {
        private IEnumerable<IFileEntry> m_local;
        private IEnumerable<IFileEntry> m_remote;
        private IList<EnumEntry> m_extra;
        private IEnumerable<DbTypes.File> m_dbsource;

        public ListSourceMerger(IEnumerable<IFileEntry> local, IEnumerable<IFileEntry> remote, IEnumerable<DbTypes.File> dbsource, IList<EnumEntry> extra)
        {
            m_local = local;
            m_remote = remote;
            m_dbsource = dbsource;
            m_extra = extra;
        }

        private string NormalizePath(string path)
        {
            return path;
        }

        #region IEnumerable implementation

        public IEnumerator<EnumEntry> GetEnumerator()
        {
            var localEnum = m_local.GetEnumerator();
            var remoteEnum = m_remote.GetEnumerator();
            var dbEnum = m_dbsource.GetEnumerator();

            var moreLocal = localEnum.MoveNext();
            var moreRemote = remoteEnum.MoveNext();
            var moreDb = dbEnum.MoveNext();

            var comparer = StringComparer.InvariantCultureIgnoreCase;

            while (moreLocal || moreRemote || moreDb || m_extra.Count > 0)
            {
                while (m_extra.Count > 0)
                {
                    var e = m_extra[0];
                    m_extra.RemoveAt(0);
                    yield return e;
                }

                if (moreLocal || moreRemote || moreDb)
                {
                    // Find the smallest path (Alphabetically)
                    string c;

                    if (moreLocal)
                        c = NormalizePath(localEnum.Current.Name);
                    else if (moreRemote)
                        c = NormalizePath(remoteEnum.Current.Name);
                    else
                        c = NormalizePath(dbEnum.Current.Path);

                    if (moreRemote && comparer.Compare(c, remoteEnum.Current.Name) > 0)
                        c = NormalizePath(remoteEnum.Current.Name);
                    if (moreDb && comparer.Compare(c, dbEnum.Current.Path) > 0)
                        c = NormalizePath(dbEnum.Current.Path);

                    var ee = new EnumEntry() { 
                        Path = c,
                        LocalTimestamp = -1,
                        RemoteTimestamp = -1,
                        LocalRecordedTimestamp = -1,
                        RemoteRecordedTimestamp = -1
                    };

                    if (moreLocal && comparer.Equals(c, NormalizePath(localEnum.Current.Name)))
                    {
                        ee.LocalTimestamp = localEnum.Current.LastModification.Ticks;
                        moreLocal = localEnum.MoveNext();
                    }

                    if (moreRemote && comparer.Equals(c, NormalizePath(remoteEnum.Current.Name)))
                    {
                        ee.RemoteTimestamp = remoteEnum.Current.LastModification.Ticks;
                        moreRemote = remoteEnum.MoveNext();
                    }

                    if (moreDb && comparer.Equals(c, NormalizePath( dbEnum.Current.Path)))
                    {
                        ee.LocalRecordedTimestamp = dbEnum.Current.Local;
                        ee.RemoteRecordedTimestamp = dbEnum.Current.Remote;
                        moreDb = dbEnum.MoveNext();
                    }

                    yield return ee;
                }
            }
        }

        #endregion

        #region IEnumerable implementation

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion
    }
}

