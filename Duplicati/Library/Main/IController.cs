//  Copyright (C) 2019, The Duplicati Team
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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Interface for the Controller class
    /// </summary>
    public interface IController : IDisposable
    {
        Library.Interface.IBackupResults Backup(string[] inputsources, Library.Utility.IFilter filter = null);
        Library.Interface.IRestoreResults Restore(string[] paths, Library.Utility.IFilter filter = null);
        Library.Interface.IRestoreControlFilesResults RestoreControlFiles(IEnumerable<string> files = null, Library.Utility.IFilter filter = null);
        Library.Interface.IDeleteResults Delete();
        Library.Interface.IRepairResults Repair(Library.Utility.IFilter filter = null);
        Library.Interface.IListResults List();
        Library.Interface.IListResults List(string filterstring);
        Library.Interface.IListResults List(IEnumerable<string> filterstrings, Library.Utility.IFilter filter);
        Library.Interface.IListResults ListControlFiles(IEnumerable<string> filterstrings, Library.Utility.IFilter filter);
        Library.Interface.IListRemoteResults ListRemote();
        Library.Interface.IListRemoteResults DeleteAllRemoteFiles();
        Library.Interface.ICompactResults Compact();
        Library.Interface.IRecreateDatabaseResults UpdateDatabaseWithVersions(Library.Utility.IFilter filter = null);
        Library.Interface.ICreateLogDatabaseResults CreateLogDatabase(string targetpath);
        Library.Interface.IListChangesResults ListChanges(string baseVersion, string targetVersion, IEnumerable<string> filterstrings = null, Library.Utility.IFilter filter = null, Action<Duplicati.Library.Interface.IListChangesResults, IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>>> callback = null);
        Library.Interface.IListAffectedResults ListAffected(List<string> args, Action<Library.Interface.IListAffectedResults> callback = null);
        Library.Interface.ITestResults Test(long samples = 1);
        Library.Interface.ITestFilterResults TestFilter(string[] paths, Library.Utility.IFilter filter = null);
        Library.Interface.ISystemInfoResults SystemInfo();
        Library.Interface.IPurgeFilesResults PurgeFiles(Library.Utility.IFilter filter);
        Library.Interface.IListBrokenFilesResults ListBrokenFiles(Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler = null);
        Library.Interface.IPurgeBrokenFilesResults PurgeBrokenFiles(Library.Utility.IFilter filter);
        Library.Interface.ISendMailResults SendMail();
        Library.Interface.IVacuumResults Vacuum();

        void Pause();
        void Resume();
        void Stop();
        void Abort();

        long MaxUploadSpeed { get; set; }
        long MaxDownloadSpeed { get; set; }
    }
}
