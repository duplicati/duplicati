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
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal static class TestHandler
    {
        public static async Task Run(long samples, string backendurl, Options options, TestResults results)
        {
            if (!System.IO.File.Exists(options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", options.Dbpath));
                                
            using (var coredb = new LocalTestDatabase(options.Dbpath))
            using (var db = new Test.TestDatabase(coredb, options))
            using (var backend = new Common.BackendHandler(options, backendurl, db, stats, reader))
            {
                db.SetResult(results);
                await db.UpdateOptionsFromDbAsync(options);
                await db.VerifyParametersAsync(options);

                if (!options.NoBackendverification)
                    await FilelistProcessor.VerifyRemoteListAsync(backend, options, db, results.BackendWriter);
                    
                await DoRunAsync(samples, options, db, stats, backend, reader);
                db.WriteResults();
            }
        }
        
        public static async Task DoRunAsync(long samples, Options options, Test.TestDatabase db, Test.TestStatsCollector stats, Common.BackendHandler backend, Common.ITaskReader taskreader)
        {
            var files = (await db.SelectTestTargetsAsync(samples, options)).ToList();

            stats.UpdatePhase(OperationPhase.Verify_Running);
            stats.UpdateProgress(0);
            var progress = 0L;
            
            if (options.FullRemoteVerification)
            {
                IAsyncDownloadedFile vol;
                using(var n = new Common.PrefetchDownloader(files, backend))
                while((vol = await n.GetNextAsync()) != null)
                {
                    try
                    {
                        if (!await taskreader.ProgressAsync)
                        {
                            await backend.ReadyAsync();
                            return;
                        }

                        progress++;
                        stats.UpdateProgress((float)progress / files.Count);

                        KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                        using(var tf = vol.TempFile)
                            res = await TestVolumeInternalsAsync(db, vol, tf, options, options.FullBlockVerification ? 1.0 : 0.2);
                        m_results.AddResult(res.Key, res.Value);

                        if (!string.IsNullOrWhiteSpace(vol.Hash) && vol.Size > 0)
                        {
                            if (res.Value == null || !res.Value.Any())
                            {
                                var rv = await db.GetRemoteVolumeAsync(vol.Name);

                                if (rv.ID < 0)
                                {
                                    if (string.IsNullOrWhiteSpace(rv.Hash) || rv.Size <= 0)
                                    {
                                        if (options.Dryrun)
                                        {
                                            m_results.AddDryrunMessage(string.Format("Sucessfully captured hash and size for {0}, would update database", vol.Name));
                                        }
                                        else
                                        {
                                            m_results.AddMessage(string.Format("Sucessfully captured hash and size for {0}, updating database", vol.Name));
                                            await db.UpdateRemoteVolumeAsync(vol.Name, RemoteVolumeState.Verified, vol.Size, vol.Hash);
                                        }
                                    }
                                }
                            }
                        }
                        
                        await db.UpdateVerificationCountAsync(vol.Name);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(vol.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                        m_results.AddError(string.Format("Failed to process file {0}", vol.Name), ex);
                        if (ex is System.Threading.ThreadAbortException)
                        {
                            await stats.SetEndTimeAsync();
                            throw;
                        }
                    }
                }
            }
            else
            {
                foreach(var f in files)
                {
                    try
                    {
                        if (!await taskreader.ProgressAsync)
                        {
                            await backend.ReadyAsync();
                            return;
                        }

                        progress++;
                        stats.UpdateProgress((float)progress / files.Count);

                        if (f.Size <= 0 || string.IsNullOrWhiteSpace(f.Hash))
                        {
                            m_results.AddMessage(string.Format("No hash or size recorded for {0}, performing full verification", f.Name));
                            KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                            string hash;
                            long size;

                            var rf = await backend.GetFileWithInfoAsync(f.Name);
                            using (var tf = rf.Item1)
                                res = await TestVolumeInternalsAsync(db, f, tf, options, 1);
                            m_results.AddResult(res.Key, res.Value);

                            if (!string.IsNullOrWhiteSpace(rf.Item3) && rf.Item2 > 0)
                            {
                                if (res.Value == null || !res.Value.Any())
                                {
                                    if (options.Dryrun)
                                    {
                                        m_results.AddDryrunMessage(string.Format("Sucessfully captured hash and size for {0}, would update database", f.Name));
                                    }
                                    else
                                    {
                                        m_results.AddMessage(string.Format("Sucessfully captured hash and size for {0}, updating database", f.Name));
                                        await db.UpdateRemoteVolumeAsync(f.Name, RemoteVolumeState.Verified, rf.Item2, rf.Item3);
                                    }
                                }
                            }
                        }
                        else
                        {
                            await backend.GetFileForTestingAsync(f.Name, f.Size, f.Hash);
                        }
                        
                        await db.UpdateVerificationCountAsync(f.Name);
                        m_results.AddResult(f.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[0]);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(f.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                        m_results.AddError(string.Format("Failed to process file {0}", f.Name), ex);
                        if (ex is System.Threading.ThreadAbortException)
                        {
                            await stats.SetEndTimeAsync();
                            throw;
                        }
                    }
                }
            }

            await stats.SetEndTimeAsync();
        }

        /// <summary>
        /// Tests the volume by examining the internal contents
        /// </summary>
        /// <param name="vol">The remote volume being examined</param>
        /// <param name="tf">The path to the downloaded copy of the file</param>
        /// <param name="sample_percent">A value between 0 and 1 that indicates how many blocks are tested in a dblock file</param>
        public static async Task<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> TestVolumeInternalsAsync(Test.TestDatabase db, IRemoteVolume vol, string tf, Options options, double sample_percent)
        {
            var blockhasher = Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm);
 
            if (blockhasher == null)
                throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(options.BlockHashAlgorithm));
            if (!blockhasher.CanReuseTransform)
                throw new UserInformationException(Strings.Common.InvalidCryptoSystem(options.BlockHashAlgorithm));
                
            var hashsize = blockhasher.HashSize / 8;
            var parsedInfo = Volumes.VolumeBase.ParseFilename(vol.Name);
            sample_percent = Math.Min(1, Math.Max(sample_percent, 0.01));

            if (parsedInfo.FileType == RemoteVolumeType.Files)
            {
                //Compare with db and see if all files are accounted for 
                // with correct file hashes and blocklist hashes
                using(var fl = await db.CreateFilelistAsync(vol.Name))
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
                        using(var bl = await db.CreateBlocklistAsync(v.Filename))
                        {
                            foreach(var h in v.Blocks)
                                bl.AddBlock(h.Key, h.Value);
                                                
                            combined = combined.Union(bl.Compare().ToArray());
                        }
                    }
                                
                using(var il = await db.CreateIndexlistAsync(vol.Name))
                {
                    foreach(var t in blocklinks)
                        il.AddBlockLink(t.Item1, t.Item2, t.Item3);
                                        
                    combined = combined.Union(il.Compare()).ToList();
                }
                                
                return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, combined.ToList());
            }
            else if (parsedInfo.FileType == RemoteVolumeType.Blocks)
            {
                using(var bl = await db.CreateBlocklistAsync(vol.Name))
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

            using(var log = new Common.LogWrapper())
                await log.WriteWarningAsync(string.Format("Unexpected file type {0} for {1}", parsedInfo.FileType, vol.Name), null);
            
            return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, null);
        }
    }
}

