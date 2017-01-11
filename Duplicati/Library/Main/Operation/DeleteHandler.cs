//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, opensource@duplicati.com
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
using System.Text;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class DeleteHandler
    {   
        private DeleteResults m_result;
        protected string m_backendurl;
        protected Options m_options;
    
        public DeleteHandler(string backend, Options options, DeleteResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public void Run()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            using(var db = new Database.LocalDeleteDatabase(m_options.Dbpath, "Delete"))
            {
                var tr = db.BeginTransaction();
                try
                {
                    m_result.SetDatabase(db);
                    Utility.UpdateOptionsFromDb(db, m_options);
                    Utility.VerifyParameters(db, m_options);
                    
                    DoRun(db, ref tr, false, false);
                    
                    if (!m_options.Dryrun)
                    {
                        using(new Logging.Timer("CommitDelete"))
                            tr.Commit();

                        db.WriteResults();
                    }
                    else
                        tr.Rollback();

                    tr = null;
                }
                finally
                {
                    if (tr != null)
                        try { tr.Rollback(); }
                        catch { }
                }
            }
        }

        public void DoRun(Database.LocalDeleteDatabase db, ref System.Data.IDbTransaction transaction, bool hasVerifiedBacked, bool forceCompact)
        {       
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                if (!hasVerifiedBacked && !m_options.NoBackendverification)
                    FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_result.BackendWriter); 
                
                var filesetNumbers = db.FilesetTimes.Zip(Enumerable.Range(0, db.FilesetTimes.Count()), (a, b) => new Tuple<long, DateTime>(b, a.Value)).ToList();
                var sets = db.FilesetTimes.Select(x => x.Value).ToArray();
                var toDelete = m_options.GetFilesetsToDelete(sets);

                if (!m_options.AllowFullRemoval && sets.Length == toDelete.Length)
                {
                    m_result.AddMessage(string.Format("Preventing removal of last fileset, use --{0} to allow removal ...", "allow-full-removal"));
                    toDelete = toDelete.Skip(1).ToArray();
                }

                if (toDelete != null && toDelete.Length > 0)
                    m_result.AddMessage(string.Format("Deleting {0} remote fileset(s) ...", toDelete.Length));

                var lst = db.DropFilesetsFromTable(toDelete, transaction).ToArray();
                foreach(var f in lst)
                    db.UpdateRemoteVolume(f.Key, RemoteVolumeState.Deleting, f.Value, null, transaction);

                if (!m_options.Dryrun)
                {
                    transaction.Commit();
                    transaction = db.BeginTransaction();
                }

                foreach(var f in lst)
                {
                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                    {
                        backend.WaitForComplete(db, transaction);
                        return;
                    }

                    if (!m_options.Dryrun)
                        backend.Delete(f.Key, f.Value);
                    else
                        m_result.AddDryrunMessage(string.Format("Would delete remote fileset: {0}", f.Key));
                }
                
                backend.WaitForComplete(db, transaction);
                
                var count = lst.Length;
                if (!m_options.Dryrun)
                {
                    if (count == 0)
                        m_result.AddMessage("No remote filesets were deleted");
                    else
                        m_result.AddMessage(string.Format("Deleted {0} remote fileset(s)", count));
                }
                else
                {
                
                    if (count == 0)
                        m_result.AddDryrunMessage("No remote filesets would be deleted");
                    else
                        m_result.AddDryrunMessage(string.Format("{0} remote fileset(s) would be deleted", count));

                    if (count > 0 && m_options.Dryrun)
                        m_result.AddDryrunMessage("Remove --dry-run to actually delete files");
                }
                
                if (!m_options.NoAutoCompact && (forceCompact || (toDelete != null && toDelete.Length > 0)))
                {
                    m_result.CompactResults = new CompactResults(m_result);
                    new CompactHandler(m_backendurl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref transaction);
                }
                
                m_result.SetResults(
                    from n in filesetNumbers
                    where toDelete.Contains(n.Item2)
                    select n, 
                    m_options.Dryrun);
            }
        }
    }
}

