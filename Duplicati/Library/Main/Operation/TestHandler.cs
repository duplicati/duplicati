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
using Duplicati.Library.Main.Database;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class TestHandler
    {
        private readonly Options m_options;
        private string m_backendurl;
        private TestResults m_results;
        
        public TestHandler(string backendurl, Options options, TestResults results)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_results = results;
        }
        
        public void Run(long samples)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));
                                
            using(var db = new LocalTestDatabase(m_options.Dbpath))
            using(var backend = new BackendManager(m_backendurl, m_options, m_results.BackendWriter, db))
            {
                db.SetResult(m_results);
                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyParameters(db, m_options);
                
                if (!m_options.NoBackendverification)
                    FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_results.BackendWriter);
                    
                DoRun(samples, db, backend);
                db.WriteResults();
            }
        }
        
        public void DoRun(long samples, LocalTestDatabase db, BackendManager backend)
        {
            var files = db.SelectTestTargets(samples, m_options).ToList();

            m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Verify_Running);
            m_results.OperationProgressUpdater.UpdateProgress(0);
            var progress = 0L;
            
            if (m_options.FullRemoteVerification)
            {
                foreach(var vol in new AsyncDownloader(files, backend))
                {
                    try
                    {
                        if (m_results.TaskControlRendevouz() == TaskControlState.Stop)
                        {
                            backend.WaitForComplete(db, null);
                            return;
                        }    

                        progress++;
                        m_results.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                        using(var tf = vol.TempFile)
                            res = TestVolumeInternals(db, vol, tf, m_options, m_results, m_options.FullBlockVerification ? 1.0 : 0.2);
                        m_results.AddResult(res.Key, res.Value);
                        
                        db.UpdateVerificationCount(vol.Name);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(vol.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                        m_results.AddError(string.Format("Failed to process file {0}", vol.Name), ex);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                    }
                }
            }
            else
            {
                foreach(var f in files)
                {
                    try
                    {   
                        if (m_results.TaskControlRendevouz() == TaskControlState.Stop)
                            return;

                        progress++;
                        m_results.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        if (f.Size < 0 || string.IsNullOrWhiteSpace(f.Hash))
                        {
                            m_results.AddMessage(string.Format("No hash recorded for {0}, performing full verification", f.Name));
                            KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                            string hash;
                            long size;

                            using (var tf = backend.GetWithInfo(f.Name, out size, out hash))
                                res = TestVolumeInternals(db, f, tf, m_options, m_results, 1);
                            m_results.AddResult(res.Key, res.Value);
                            
                            if (res.Value != null && !res.Value.Any() && !string.IsNullOrWhiteSpace(hash))
                            {
                                if (!m_options.Dryrun)
                                {
                                    m_results.AddMessage(string.Format("Sucessfully captured hash for {0}, updating database", f.Name));
                                    db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Verified, size, hash);
                                }
                            }
                        }
                        else
                            backend.GetForTesting(f.Name, f.Size, f.Hash);
                        db.UpdateVerificationCount(f.Name);
                        m_results.AddResult(f.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[0]);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(f.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                        m_results.AddError(string.Format("Failed to process file {0}", f.Name), ex);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                    }
                }
            }
        }

        /// <summary>
        /// Tests the volume by examining the internal contents
        /// </summary>
        /// <param name="vol">The remote volume being examined</param>
        /// <param name="tf">The path to the downloaded copy of the file</param>
        /// <param name="sample_percent">A value between 0 and 1 that indicates how many blocks are tested in a dblock file</param>
        public static KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> TestVolumeInternals(LocalTestDatabase db, IRemoteVolume vol, string tf, Options options, ILogWriter log, double sample_percent)
        {
            var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(options.BlockHashAlgorithm);
 
            if (blockhasher == null)
                throw new Exception(Strings.Common.InvalidHashAlgorithm(options.BlockHashAlgorithm));
            if (!blockhasher.CanReuseTransform)
                throw new Exception(Strings.Common.InvalidCryptoSystem(options.BlockHashAlgorithm));
                
            var hashsize = blockhasher.HashSize / 8;
            var parsedInfo = Volumes.VolumeBase.ParseFilename(vol.Name);
            sample_percent = Math.Min(1, Math.Max(sample_percent, 0.01));

            if (parsedInfo.FileType == RemoteVolumeType.Files)
            {
                //Compare with db and see if all files are accounted for 
                // with correct file hashes and blocklist hashes
                using(var fl = db.CreateFilelist(vol.Name))
                {
                    using(var rd = new Volumes.FilesetVolumeReader(parsedInfo.CompressionModule, tf, options))
                        foreach(var f in rd.Files)
                            fl.Add(f.Path, f.Size, f.Hash, f.Metasize, f.Metahash, f.BlocklistHashes, f.Type, f.Time);
                                    
                    return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, fl.Compare().ToList());
                }       
            }
            else if (parsedInfo.FileType == RemoteVolumeType.Index)
            {
                var blocklinks = new List<Tuple<string, string, long>>();
                IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> combined = new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[0]; 
                                
                //Compare with db and see that all hashes and volumes are listed
                using(var rd = new Volumes.IndexVolumeReader(parsedInfo.CompressionModule, tf, options, hashsize))
                    foreach(var v in rd.Volumes)
                    {
                        blocklinks.Add(new Tuple<string, string, long>(v.Filename, v.Hash, v.Length));
                        using(var bl = db.CreateBlocklist(v.Filename))
                        {
                            foreach(var h in v.Blocks)
                                bl.AddBlock(h.Key, h.Value);
                                                
                            combined = combined.Union(bl.Compare().ToArray());
                        }
                    }
                                
                using(var il = db.CreateIndexlist(vol.Name))
                {
                    foreach(var t in blocklinks)
                        il.AddBlockLink(t.Item1, t.Item2, t.Item3);
                                        
                    combined = combined.Union(il.Compare()).ToList();
                }
                                
                return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, combined.ToList());
            }
            else if (parsedInfo.FileType == RemoteVolumeType.Blocks)
            {
                using(var bl = db.CreateBlocklist(vol.Name))
                using(var rd = new Volumes.BlockVolumeReader(parsedInfo.CompressionModule, tf, options))
                {                                    
                    //Verify that all blocks are in the file
                    foreach(var b in rd.Blocks)
                        bl.AddBlock(b.Key, b.Value);
    
                    //Select random blocks and verify their hashes match the filename and size
                    var hashsamples = new List<KeyValuePair<string, long>>(rd.Blocks);
                    var sampleCount = Math.Min(Math.Max(0, (int)(hashsamples.Count * sample_percent)), hashsamples.Count - 1);
                    var rnd = new Random();
                                     
                    while (hashsamples.Count > sampleCount)
                        hashsamples.RemoveAt(rnd.Next(hashsamples.Count));
    
                    var blockbuffer = new byte[options.Blocksize];
                    var changes = new List<KeyValuePair<Library.Interface.TestEntryStatus, string>>();
                    foreach(var s in hashsamples)
                    {
                        var size = rd.ReadBlock(s.Key, blockbuffer);
                        if (size != s.Value)
                            changes.Add(new KeyValuePair<Library.Interface.TestEntryStatus, string>(Library.Interface.TestEntryStatus.Modified, s.Key));
                        else
                        {
                            var hash = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                            if (hash != s.Key)
                                changes.Add(new KeyValuePair<Library.Interface.TestEntryStatus, string>(Library.Interface.TestEntryStatus.Modified, s.Key));
                        }
                    }
                                    
                    return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, changes.Union(bl.Compare().ToList()));
                }                                
            }

            log.AddWarning(string.Format("Unexpected file type {0} for {1}", parsedInfo.FileType, vol.Name), null);
            return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, null);
        }
    }
}

