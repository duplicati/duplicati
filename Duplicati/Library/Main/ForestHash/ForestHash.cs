using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.ForestHash.Database;
using Newtonsoft.Json;

namespace Duplicati.Library.Main.ForestHash
{
    internal class ForestHash
    {
        /// <summary>
        /// Runs a backup
        /// </summary>
        /// <param name="remoteurl">The destination url</param>
        /// <param name="options">The input options</param>
        /// <param name="stat">The status report instance</param>
        /// <param name="sources">The list of folders to back up</param>
        public static void Backup(string remoteurl, Options options, BackupStatistics stat, string[] sources)
        {
            using (var h = new Operation.BackupHandler(remoteurl, new FhOptions(options.RawOptions), stat, sources))
                h.Run();
        }

        public static void Restore(string remoteurl, Options options, RestoreStatistics stat, string target)
        {
            using (var h = new Operation.RestoreHandler(remoteurl, new FhOptions(options.RawOptions), stat, target))
                h.Run();
        }

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        public static void VerifyRemoteList(FhBackend backend, Options options, LocalDatabase database, CommunicationStatistics stat)
		{
			var tp = RemoteListAnalysis(backend, options, database);
			long extraCount = 0;
			long missingCount = 0;
			
			foreach(var n in tp.ExtraVolumes)
			{
				if (!options.QuietConsole)
					stat.LogMessage(string.Format("Extra unknown file: {0}", n.File.Name));
				stat.LogWarning(string.Format("Extra unknown file: {0}", n.File.Name), null);
				extraCount++;
			}

			foreach(var n in tp.MissingVolumes)
			{
				if (!options.QuietConsole)
					stat.LogMessage(string.Format("Missing file: {0}", n.Name));
				stat.LogWarning(string.Format("Missing file: {0}", n.Name), null);
				missingCount++;
			}

            if (extraCount > 0)
                throw new Exception(string.Format("Found {0} remote files that are not recorded in local storage, please run cleanup", extraCount));

            if (missingCount > 0)
            {
            	if (!tp.BackupPrefixes.Contains(options.BackupPrefix) && tp.BackupPrefixes.Length > 0)
                	throw new Exception(string.Format("Found {0} files that are missing from the remote storage, and no files with the backup prefix {1}, but found the following backup prefixes: {2}", missingCount, options.BackupPrefix, string.Join(", ", tp.BackupPrefixes)));
            	else
                	throw new Exception(string.Format("Found {0} files that are missing from the remote storage, please run cleanup", missingCount));
            }
		}
		
		public struct RemoteAnalysisResult
		{
			public IEnumerable<Volumes.IParsedVolume> ParsedVolumes;
			public IEnumerable<Volumes.IParsedVolume> ExtraVolumes;
			public IEnumerable<RemoteVolumeEntry> MissingVolumes;
			
			public string[] BackupPrefixes { get { return ParsedVolumes.Select(x => x.Prefix).Distinct().ToArray(); } }
		}

        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        public static RemoteAnalysisResult RemoteListAnalysis(FhBackend backend, Options options, LocalDatabase database)
        {
            var rawlist = backend.List();
            var lookup = new Dictionary<string, Volumes.IParsedVolume>();

			var remotelist = from n in rawlist let p = Volumes.VolumeBase.ParseFilename(n) where p != null select p;
            foreach (var s in remotelist)
            	if (s.Prefix == options.BackupPrefix)
                	lookup[s.File.Name] = s;

            var missing = new List<RemoteVolumeEntry>();
            var locallist = database.GetRemoteVolumes();
            foreach (var i in locallist)
            {
            	//Ignore those that are deleted
            	if (i.State == RemoteVolumeState.Deleted)
            		continue;
            		
                if (i.State == RemoteVolumeState.Temporary)
                {
                    database.LogMessage("info", string.Format("removing file listed as {0}: {1}", i.State, i.Name), null, null);
                    database.RemoveRemoteVolume(i.Name, null);
                }
                else
                {
                    Volumes.IParsedVolume r;
                    if (!lookup.TryGetValue(i.Name, out r))
                    {
                        if (i.State == RemoteVolumeState.Uploading || i.State == RemoteVolumeState.Deleting || (r != null && r.File.Size != i.Size && r.File.Size >= 0 && i.Size >= 0))
                        {
                            database.LogMessage("info", string.Format("removing file listed as {0}: {1}", i.State, i.Name), null, null);
                            database.RemoveRemoteVolume(i.Name, null);
                        }
                        else
                            missing.Add(i);
                    }
                    else if (i.State != RemoteVolumeState.Verified)
                    {
                        database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash);
                    }

                    lookup.Remove(i.Name);
                }
            }
            
            return new RemoteAnalysisResult() { ParsedVolumes = remotelist, ExtraVolumes = lookup.Values, MissingVolumes = missing };
        }

        /// <summary>
        /// Interface for describing metadata
        /// </summary>
        public interface IMetahash
        {
            /// <summary>
            /// The base64 encoded hash of the metadata
            /// </summary>
            string Hash { get; }
            /// <summary>
            /// The size of the metadata in bytes
            /// </summary>
            long Size { get; }
            /// <summary>
            /// The UTF-8 encoded json element with the metadata
            /// </summary>
            byte[] Blob { get; }
            /// <summary>
            /// The lookup table of contained metadata values
            /// </summary>
            Dictionary<string, string> Values { get; }
        }

        /// <summary>
        /// Implementation of the IMetahash interface
        /// </summary>
        private class Metahash : IMetahash
        {
            /// <summary>
            /// The base64 encoded hash
            /// </summary>
            private readonly string m_hash;
            /// <summary>
            /// The UTF-8 encoded json element with the metadata
            /// </summary>
            private readonly byte[] m_blob;
            /// <summary>
            /// The lookup table with elements
            /// </summary>
            private readonly Dictionary<string, string> m_values;

            public Metahash(Dictionary<string, string> values, FhOptions options)
            {
                m_values = values;
                var hasher = System.Security.Cryptography.HashAlgorithm.Create(options.FhBlockHashAlgorithm);
				if (hasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, options.FhBlockHashAlgorithm));
				if (!hasher.CanReuseTransform)
					throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, options.FhBlockHashAlgorithm));
					
                using (var ms = new System.IO.MemoryStream())
                using (var w = new StreamWriter(ms, Encoding.UTF8))
                {
                    w.Write(JsonConvert.SerializeObject(values));
                    w.Flush();

                    m_blob = ms.ToArray();

                    ms.Position = 0;
                    m_hash = Convert.ToBase64String(hasher.ComputeHash(ms));
                }
            }

            public string Hash
            {
                get { return m_hash; }
            }

            public long Size
            {
                get { return m_blob.Length; }
            }

            public byte[] Blob
            {
                get { return m_blob; }
            }

            public Dictionary<string, string> Values
            {
                get { return m_values; }
            }
        }

        /// <summary>
        /// Constructs a container for a given metadata dictionary
        /// </summary>
        /// <param name="values">The metadata values to wrap</param>
        /// <returns>A IMetahash instance</returns>
        public static IMetahash WrapMetadata(Dictionary<string, string> values, FhOptions options)
        {
            return new Metahash(values, options);
        }

		internal static void VerifyParameters(LocalDatabase db, FhOptions options)
		{
			var newDict = new Dictionary<string, string>();
			newDict.Add("blocksize", options.Fhblocksize.ToString());
			newDict.Add("blockhash", options.FhBlockHashAlgorithm);
			newDict.Add("filehash", options.FhFileHashAlgorithm);
			
		
			var opts = db.GetDbOptions();
			var needsUpdate = false;
			foreach(var k in newDict)
				if (!opts.ContainsKey(k.Key))
					needsUpdate = true;
				else if (opts[k.Key] != k.Value)
					throw new Exception(string.Format("Unsupported change of parameter {0} from {1} to {2}", k.Key, opts[k.Key], k.Value));
		
			//Extra sanity check
			if (db.GetBlocksLargerThan(options.Fhblocksize) > 0)
				throw new Exception("Unsupported block-size change detected");
		
			if (needsUpdate)
				db.SetDbOptions(newDict);				
		}


        internal static IEnumerable<Volumes.IParsedVolume> ParseFileList(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            var opts = new FhOptions(options);
            using (var db = new LocalDatabase(opts.Fhdbpath, "ParseFileList"))
            using (var b = new FhBackend(target, opts, stat, db))
            {
                var res = 
                    from n in b.List()
                    let np = Volumes.VolumeBase.ParseFilename(n)
                    where np != null
                    select np;
                    
                b.WaitForComplete(db, null);
                
                return res;
            }
        }

        internal static string CompactBlocks(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            using(var h = new Operation.CompactHandler(target, new FhOptions(options), stat))
            	return h.Run();
        }

        internal static string RecreateDatabase(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
        	var opts = new FhOptions(options);        		
            using(var h = new Operation.RecreateDatabaseHandler(target, opts, stat))
            	h.Run(opts.Fhdbpath);
            	
            return "Recreate Completed";
        }

		public static string DeleteFilesets(string target, string filesets, Dictionary<string, string> options, CommunicationStatistics stat)
		{
        	var opts = new FhOptions(options);
            using(var h = new Operation.DeleteHandler(target, opts, stat))
            {
            	h.Filesets = filesets;
            	return h.Run();
            }
		}

		public static string CreateLogDatabase(string target, Dictionary<string, string> options, CommunicationStatistics stat)
		{
        	var opts = new FhOptions(options);        		
            using(var h = new Operation.CreateBugReportHandler(target, opts, stat))
            	h.Run();
            	
            return "Create db Completed! Please verify that the database contains no sensitive information before sending!";
		}

    }
}
