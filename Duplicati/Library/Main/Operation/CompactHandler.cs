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
using System.Collections.Generic;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using System.Text;

namespace Duplicati.Library.Main.Operation
{
    internal class CompactHandler
    {
        protected string m_backendurl;
        protected Options m_options;
        protected CompactResults m_result;
        
        public CompactHandler(string backend, Options options, CompactResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }
        
        public virtual void Run()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));
            
            using(var db = new LocalDeleteDatabase(m_options.Dbpath, true))
            {
                var tr = db.BeginTransaction();
                try
                {
                    m_result.SetDatabase(db);
                    Utility.UpdateOptionsFromDb(db, m_options);
                    Utility.VerifyParameters(db, m_options);
                    
                    var changed = DoCompact(db, false, ref tr);
                    
                    if (changed && m_options.UploadVerificationFile)
                        FilelistProcessor.UploadVerificationFile(m_backendurl, m_options, m_result.BackendWriter, db, null);
                    
                    if (!m_options.Dryrun)
                    {
                        using(new Logging.Timer("CommitCompact"))
                            tr.Commit();
                        if (changed)
                        {
                            db.WriteResults();                    
                            db.Vacuum();
                        }
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
        
        internal bool DoCompact(LocalDeleteDatabase db, bool hasVerifiedBackend, ref System.Data.IDbTransaction transaction)
        {
            var report = db.GetCompactReport(m_options.VolumeSize, m_options.Threshold, m_options.SmallFileSize, m_options.SmallFileMaxCount, transaction);
            report.ReportCompactData(m_result);
            
            if (report.ShouldReclaim || report.ShouldCompact)
            {
                using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
                {
                    if (!hasVerifiedBackend && !m_options.NoBackendverification)
                        FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_result.BackendWriter);
        
                    BlockVolumeWriter newvol = new BlockVolumeWriter(m_options);
                    newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
    
                    IndexVolumeWriter newvolindex = null;
                    if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                    {
                        newvolindex = new IndexVolumeWriter(m_options);
                        newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
                        db.AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID, transaction);
                        newvolindex.StartVolume(newvol.RemoteFilename);
                    }
                    
                    long blocksInVolume = 0;
                    long discardedBlocks = 0;
                    long discardedSize = 0;
                    byte[] buffer = new byte[m_options.Blocksize];
                    var remoteList = db.GetRemoteVolumes().Where(n => n.State == RemoteVolumeState.Uploaded || n.State == RemoteVolumeState.Verified).ToArray();
                    
                    //These are for bookkeeping
                    var uploadedVolumes = new List<KeyValuePair<string, long>>();
                    var deletedVolumes = new List<KeyValuePair<string, long>>();
                    var downloadedVolumes = new List<KeyValuePair<string, long>>();
                    
                    //We start by deleting unused volumes to save space before uploading new stuff
                    var fullyDeleteable = (from v in remoteList
                                           where report.DeleteableVolumes.Contains(v.Name)
                                           select (IRemoteVolume)v).ToList();                    
                    deletedVolumes.AddRange(DoDelete(db, backend, fullyDeleteable, ref transaction));

                    // This list is used to pick up unused volumes,
                    // so they can be deleted once the upload of the
                    // required fragments is complete
                    var deleteableVolumes = new List<IRemoteVolume>();

                    if (report.ShouldCompact)
                    {
                        var volumesToDownload = (from v in remoteList
                                                 where report.CompactableVolumes.Contains(v.Name)
                                                 select (IRemoteVolume)v).ToList();
                        
                        using(var q = db.CreateBlockQueryHelper(m_options, transaction))
                        {
                            foreach(var entry in new AsyncDownloader(volumesToDownload, backend))
                            using(var tmpfile = entry.TempFile)
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(db, transaction);
                                    return false;
                                }
                                    
                                downloadedVolumes.Add(new KeyValuePair<string, long>(entry.Name, entry.Size));
                                var inst = VolumeBase.ParseFilename(entry.Name);
                                using(var f = new BlockVolumeReader(inst.CompressionModule, tmpfile, m_options))
                                {
                                    foreach(var e in f.Blocks)
                                    {
                                        if (q.UseBlock(e.Key, e.Value, transaction))
                                        {
                                            //TODO: How do we get the compression hint? Reverse query for filename in db?
                                            var s = f.ReadBlock(e.Key, buffer);
                                            if (s != e.Value)
                                                throw new Exception(string.Format("Size mismatch problem for block {0}, {1} vs {2}", e.Key, s, e.Value));
                                                
                                            newvol.AddBlock(e.Key, buffer, 0, s, Duplicati.Library.Interface.CompressionHint.Compressible);
                                            if (newvolindex != null)
                                                newvolindex.AddBlock(e.Key, e.Value);
                                                
                                            db.MoveBlockToNewVolume(e.Key, e.Value, newvol.VolumeID, transaction);
                                            blocksInVolume++;
                                            
                                            if (newvol.Filesize > m_options.VolumeSize)
                                            {
                                                uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
                                                if (newvolindex != null)
                                                    uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, new System.IO.FileInfo(newvolindex.LocalFilename).Length));
    
                                                if (!m_options.Dryrun)
                                                    backend.Put(newvol, newvolindex);
                                                else
                                                    m_result.AddDryrunMessage(string.Format("Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length)));
                                                
                                                
                                                newvol = new BlockVolumeWriter(m_options);
                                                newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
                
                                                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                                                {
                                                    newvolindex = new IndexVolumeWriter(m_options);
                                                    newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
                                                    db.AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID, transaction);
                                                    newvolindex.StartVolume(newvol.RemoteFilename);
                                                }
                                                
                                                blocksInVolume = 0;
                                                
                                                //After we upload this volume, we can delete all previous encountered volumes
                                                deletedVolumes.AddRange(DoDelete(db, backend, deleteableVolumes, ref transaction));
                                                deleteableVolumes = new List<IRemoteVolume>();
                                            }
                                        }
                                        else
                                        {
                                            discardedBlocks++;
                                            discardedSize += e.Value;
                                        }
                                    }
                                }
    
                                deleteableVolumes.Add(entry);
                            }
                            
                            if (blocksInVolume > 0)
                            {
                                uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
                                if (newvolindex != null)
                                    uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, new System.IO.FileInfo(newvolindex.LocalFilename).Length));
                                if (!m_options.Dryrun)
                                    backend.Put(newvol, newvolindex);
                                else
                                    m_result.AddDryrunMessage(string.Format("Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length)));
                            }
                            else
                            {
                                db.RemoveRemoteVolume(newvol.RemoteFilename, transaction);
                                if (newvolindex != null)
                                {
                                    db.RemoveRemoteVolume(newvolindex.RemoteFilename, transaction);
                                    newvolindex.FinishVolume(null, 0);
                                }
                            }
                        }
                    }
                    
                    deletedVolumes.AddRange(DoDelete(db, backend, deleteableVolumes, ref transaction));
                                        
                    var downloadSize = downloadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a,x) => a + x.Value);
                    var deletedSize = deletedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a,x) => a + x.Value);
                    var uploadSize = uploadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a,x) => a + x.Value);
                    
                    m_result.DeletedFileCount = deletedVolumes.Count;
                    m_result.DownloadedFileCount = downloadedVolumes.Count;
                    m_result.UploadedFileCount = uploadedVolumes.Count;
                    m_result.DeletedFileSize = deletedSize;
                    m_result.DownloadedFileSize = downloadSize;
                    m_result.UploadedFileSize = uploadSize;
                    m_result.Dryrun = m_options.Dryrun;
                    
                    if (m_result.Dryrun)
                    {
                        if (downloadedVolumes.Count == 0)
                            m_result.AddDryrunMessage(string.Format("Would delete {0} files, which would reduce storage by {1}", m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize)));
                        else
                            m_result.AddDryrunMessage(string.Format("Would download {0} file(s) with a total size of {1}, delete {2} file(s) with a total size of {3}, and compact to {4} file(s) with a size of {5}, which would reduce storage by {6} file(s) and {7}", m_result.DownloadedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DownloadedFileSize), m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize), m_result.UploadedFileCount, Library.Utility.Utility.FormatSizeString(m_result.UploadedFileSize), m_result.DeletedFileCount - m_result.UploadedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize - m_result.UploadedFileSize)));
                    }
                    else
                    {
                        if (m_result.DownloadedFileCount == 0)
                            m_result.AddMessage(string.Format("Deleted {0} files, which reduced storage by {1}", m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize)));
                        else
                            m_result.AddMessage(string.Format("Downloaded {0} file(s) with a total size of {1}, deleted {2} file(s) with a total size of {3}, and compacted to {4} file(s) with a size of {5}, which reduced storage by {6} file(s) and {7}", m_result.DownloadedFileCount, Library.Utility.Utility.FormatSizeString(downloadSize), m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize), m_result.UploadedFileCount, Library.Utility.Utility.FormatSizeString(m_result.UploadedFileSize), m_result.DeletedFileCount - m_result.UploadedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize - m_result.UploadedFileSize)));
                    }
                            
                    backend.WaitForComplete(db, transaction);
                }

                m_result.EndTime = DateTime.UtcNow;
                return (m_result.DeletedFileCount + m_result.UploadedFileCount) > 0;
            }
            else
            {
                m_result.EndTime = DateTime.UtcNow;
                return false;
            }
        }

        private IEnumerable<KeyValuePair<string, long>> DoDelete(LocalDeleteDatabase db, BackendManager backend, IEnumerable<IRemoteVolume> deleteableVolumes, ref System.Data.IDbTransaction transaction)
        {
            // Mark all volumes as disposable
            foreach(var f in deleteableVolumes)
                db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash, transaction);

            // Before we commit the current state, make sure the backend has caught up
            backend.WaitForEmpty(db, transaction);

            if (!m_options.Dryrun)
            {
                transaction.Commit();
                transaction = db.BeginTransaction();
            }

            return PerformDelete(backend, db.GetDeletableVolumes(deleteableVolumes, transaction));
        }
            
        
        private IEnumerable<KeyValuePair<string, long>> PerformDelete(BackendManager backend, IEnumerable<IRemoteVolume> list)
        {
            foreach(var f in list)
            {
                if (!m_options.Dryrun)
                    backend.Delete(f.Name, f.Size);
                else
                    m_result.AddDryrunMessage(string.Format("Would delete remote file: {0}, size: {1}", f.Name, Library.Utility.Utility.FormatSizeString(f.Size)));

                yield return new KeyValuePair<string, long>(f.Name, f.Size);
            }               
        }
    }
}

