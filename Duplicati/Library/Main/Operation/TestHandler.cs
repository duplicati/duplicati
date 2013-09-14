//  Copyright (C) 2011, Kenneth Skovhede

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
using Duplicati.Library.Main.Database;
using System.Linq;
using System.Collections.Generic;

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
                Utility.VerifyParameters(db, m_options);
                
                if (!m_options.NoBackendverification)
                    FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_results.BackendWriter);
                    
                DoRun(samples, db, backend);
            }
        }
        
        public void DoRun(long samples, LocalTestDatabase db, BackendManager backend)
        {

            var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
 
            if (blockhasher == null)
                throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
            if (!blockhasher.CanReuseTransform)
                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.BlockHashAlgorithm));
                
            var hashsize = blockhasher.HashSize / 8;
            var files = db.SelectTestTargets(samples, m_options).ToList();
            
            foreach(var vol in new AsyncDownloader(files, backend))
            {
                var parsedInfo = Volumes.VolumeBase.ParseFilename(vol.Name);
                try
                {
                    using(var tf = vol.TempFile)
                    {
                        if (parsedInfo.FileType == RemoteVolumeType.Files)
                        {
                            //Compare with db and see if all files are accounted for 
                            // with correct file hashes and blocklist hashes
                            using(var fl = db.CreateFilelist(vol.Name))
                            {
                                using(var rd = new Volumes.FilesetVolumeReader(parsedInfo.CompressionModule, tf, m_options))
                                    foreach(var f in rd.Files)
                                        fl.Add(f.Path, f.Size, f.Hash, f.Metasize, f.Metahash, f.BlocklistHashes, f.Type, f.Time);
                                
                                m_results.AddResult(vol.Name, fl.Compare().ToList());
                            }       
                        }
                        else if (parsedInfo.FileType == RemoteVolumeType.Index)
                        {
                            var blocklinks = new List<Tuple<string, string, long>>();
                            IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> combined = new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[0]; 
                            
                            //Compare with db and see that all hashes and volumes are listed
                            using(var rd = new Volumes.IndexVolumeReader(parsedInfo.CompressionModule, tf, m_options, hashsize))
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
                            
                            m_results.AddResult(vol.Name, combined.ToList());
                        }
                        else if (parsedInfo.FileType == RemoteVolumeType.Blocks)
                        {
                            using(var bl = db.CreateBlocklist(vol.Name))
                            using(var rd = new Volumes.BlockVolumeReader(parsedInfo.CompressionModule, tf, m_options))
                            {                                    
                                //Verify that all blocks are in the file
                                foreach(var b in rd.Blocks)
                                    bl.AddBlock(b.Key, b.Value);

                                //Select 20% random blocks and verify their hashes match the filename and size
                                var hashsamples = new List<KeyValuePair<string, long>>(rd.Blocks);
                                var sampleCount = Math.Min(Math.Max(0, (int)(hashsamples.Count * 0.2)), hashsamples.Count - 1);
                                var rnd = new Random();
                                 
                                while(hashsamples.Count > sampleCount)
                                    hashsamples.RemoveAt(rnd.Next(hashsamples.Count));

                                var blockbuffer = new byte[m_options.Blocksize];
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
                                
                                m_results.AddResult(vol.Name, changes.Union(bl.Compare().ToList()));
                            }
                            
                        }
                    }
                    
                    db.UpdateVerificationCount(vol.Name);
                }
                catch (Exception ex)
                {
                    m_results.AddResult(vol.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                    m_results.AddError(string.Format("Failed to process file {0}", vol.Name), ex);
                }
            }
        }
    }
}

