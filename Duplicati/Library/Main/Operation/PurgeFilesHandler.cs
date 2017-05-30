//  Copyright (C) 2016, The Duplicati Team
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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class PurgeFilesHandler
    {
        protected string m_backendurl;
        protected Options m_options;
        protected PurgeFilesResults m_result;

        public PurgeFilesHandler(string backend, Options options, PurgeFilesResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public void Run(Library.Utility.IFilter filter)
        {
            if (filter == null || filter.Empty)
                throw new UserInformationException("Cannot purge with an empty filter, as that would cause all files to be removed.\nTo remove an entire backup set, use the \"delete\" command.");

            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath));
            
            using (var db = new Database.LocalPurgeDatabase(m_options.Dbpath))
                DoRun(db, filter, null, 0, 1);
        }

        public void Run(Database.LocalPurgeDatabase db, float pgoffset, float pgspan, Action<System.Data.IDbCommand, long, string> filtercommand)
        {
            DoRun(db, null, filtercommand, pgoffset, pgspan);
        }

        private void DoRun(Database.LocalPurgeDatabase db, Library.Utility.IFilter filter, Action<System.Data.IDbCommand, long, string> filtercommand, float pgoffset, float pgspan)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Begin);
            m_result.AddMessage("Starting purge operation");

            var doCompactStep = !m_options.NoAutoCompact && filtercommand == null;

            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                if (db.PartiallyRecreated)
                    throw new UserInformationException("The purge command does not work on partially recreated databases");

                if (db.RepairInProgress && filtercommand == null)
                    throw new UserInformationException(string.Format("The purge command does not work on an incomplete database, try the {0} operation.", "purge-broken-files"));

                var versions = db.GetFilesetIDs(m_options.Time, m_options.Version).ToArray();
                if (versions.Length <= 0)
                    throw new UserInformationException("No filesets matched the supplied time or versions");

                var orphans = db.CountOrphanFiles(null);
                if (orphans != 0)
                    throw new UserInformationException(string.Format("Unable to start the purge process as there are {0} orphan file(s)", orphans));

                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyParameters(db, m_options);

                if (filtercommand == null)
                {
                    db.VerifyConsistency(null, m_options.Blocksize, m_options.BlockhashSize, false);

                    if (m_options.NoBackendverification)
                        FilelistProcessor.VerifyLocalList(backend, m_options, db, m_result.BackendWriter);
                    else
                        FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_result.BackendWriter, null);
                }

                var filesets = db.FilesetTimes.ToArray();

                var versionprogress = ((doCompactStep ? 0.75f : 1.0f) / versions.Length) * pgspan;
                var currentprogress = pgoffset;

                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Process);
                m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

                // Reverse makes sure we re-write the old versions first
                foreach (var versionid in versions.Reverse())
                {
                    using (var tr = db.BeginTransaction())
                    {
                        var ix = -1;
                        for (var i = 0; i < filesets.Length; i++)
                            if (filesets[i].Key == versionid)
                            {
                                ix = i;
                                break;
                            }

                        if (ix < 0)
                            throw new InvalidProgramException(string.Format("Fileset was reported with id {0}, but could not be found?", versionid));

                        var secs = 0;
                        while (secs < 60)
                        {
                            secs++;
                            var tfn = Volumes.VolumeBase.GenerateFilename(RemoteVolumeType.Files, m_options, null, filesets[ix].Value.AddSeconds(secs));
                            if (db.GetRemoteVolumeID(tfn, tr) < 0)
                                break;
                        }

                        var ts = filesets[ix].Value.AddSeconds(secs);
                        var prevfilename = db.GetRemoteVolumeNameForFileset(filesets[ix].Key, tr);

                        if (secs >= 60)
                            throw new Exception(string.Format("Unable to create a new fileset for {0} because the resulting timestamp {1} is more than 60 seconds away", prevfilename, ts));

                        if (ix != 0 && filesets[ix - 1].Value <= ts)
                            throw new Exception(string.Format("Unable to create a new fileset for {0} because the resulting timestamp {1} is larger than the next timestamp {2}", prevfilename, ts, filesets[ix - 1].Value));

                        using (var tempset = db.CreateTemporaryFileset(versionid, tr))
                        {
                            if (filtercommand == null)
                                tempset.ApplyFilter(filter);
                            else
                                tempset.ApplyFilter(filtercommand);

                            if (tempset.RemovedFileCount == 0)
                            {
                                m_result.AddMessage(string.Format("Not writing a new fileset for {0} as it was not changed", prevfilename));
                                currentprogress += versionprogress;
                                tr.Rollback();
                                continue;
                            }
                            else
                            {
                                using (var tf = new Library.Utility.TempFile())
                                using (var vol = new Volumes.FilesetVolumeWriter(m_options, ts))
                                {
                                    var newids = tempset.ConvertToPermanentFileset(vol.RemoteFilename, ts);
                                    vol.VolumeID = newids.Item1;

                                    m_result.AddMessage(string.Format("Replacing fileset {0} with {1} which has with {2} fewer file(s) ({3} reduction)", prevfilename, vol.RemoteFilename, tempset.RemovedFileCount, Library.Utility.Utility.FormatSizeString(tempset.RemovedFileSize)));

                                    db.WriteFileset(vol, tr, newids.Item2);

                                    m_result.RemovedFileSize += tempset.RemovedFileSize;
                                    m_result.RemovedFileCount += tempset.RemovedFileCount;
                                    m_result.RewrittenFileLists++;

                                    currentprogress += (versionprogress / 2);
                                    m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

                                    if (m_options.Dryrun || m_options.Verbose || Logging.Log.LogLevel == Logging.LogMessageType.Profiling)
                                    {
                                        foreach (var fe in tempset.ListAllDeletedFiles())
                                        {
                                            var msg = string.Format("  Purging file {0} ({1})", fe.Key, Library.Utility.Utility.FormatSizeString(fe.Value));

                                            if (Logging.Log.LogLevel == Logging.LogMessageType.Profiling)
                                                Logging.Log.WriteMessage(msg, Logging.LogMessageType.Profiling);

                                            if (m_options.Dryrun)
                                                m_result.AddDryrunMessage(msg);
                                            else if (m_options.Verbose)
                                                m_result.AddVerboseMessage(msg);
                                        }

                                        if (m_options.Dryrun)
                                            m_result.AddDryrunMessage("Writing files to remote storage");
                                        else if (m_options.Verbose)
                                            m_result.AddVerboseMessage("Writing files to remote storage");
                                    }

                                    if (m_options.Dryrun)
                                    {
                                        m_result.AddDryrunMessage(string.Format("Would upload file {0} ({1}) and delete file {2}, removing {3} files", vol.RemoteFilename, Library.Utility.Utility.FormatSizeString(vol.Filesize), prevfilename, tempset.RemovedFileCount));
                                        tr.Rollback();
                                    }
                                    else
                                    {
                                        var lst = db.DropFilesetsFromTable(new[] { filesets[ix].Value }, tr).ToArray();
                                        foreach (var f in lst)
                                            db.UpdateRemoteVolume(f.Key, RemoteVolumeState.Deleting, f.Value, null, tr);

                                        tr.Commit();
                                        backend.Put(vol, synchronous: true);
                                        backend.Delete(prevfilename, -1, true);
                                        backend.FlushDbMessages();
                                    }
                                }
                            }
                        }
                    }

                    currentprogress += (versionprogress / 2);
                    m_result.OperationProgressUpdater.UpdateProgress(currentprogress);
                }


                if (doCompactStep)
                {
                    if (m_result.RewrittenFileLists == 0)
                    {
                        m_result.AddMessage("Skipping compacting as no new volumes were written");
                    }
                    else
                    {
                        m_result.OperationProgressUpdater.UpdateProgress(pgoffset + (0.75f * pgspan));
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Compact);
                        m_result.CompactResults = new CompactResults(m_result);
                        using (var cdb = new Database.LocalDeleteDatabase(db))
                        {
                            var tr = cdb.BeginTransaction();
                            try
                            {
                                new CompactHandler(backend.BackendUrl, m_options, (CompactResults)m_result.CompactResults).DoCompact(cdb, true, ref tr);
                            }
                            catch
                            {
                                try { tr.Rollback(); }
                                catch { }
                            }
                            finally
                            {
                                try { tr.Commit(); }
                                catch { }
                            }
                        }
                    }

                    m_result.OperationProgressUpdater.UpdateProgress(pgoffset + pgspan);
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Complete);
                }

                backend.WaitForComplete(db, null);
            }
        }
    }
}
