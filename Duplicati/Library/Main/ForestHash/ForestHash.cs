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
			var extra = tp.Item1;
			var missing = tp.Item2;
			long extraCount = 0;
			long missingCount = 0;
			
			foreach(var n in extra)
			{
				stat.LogWarning(string.Format("Extra unknown file: {0}", n.Name), null);
				extraCount++;
			}

			foreach(var n in missing)
			{
				stat.LogWarning(string.Format("Missing file: {0}", n.Name), null);
				missingCount++;
			}

            if (extraCount > 0)
                throw new Exception(string.Format("Found {0} remote files that are not recorded in local storage, please run cleanup", extraCount));

            if (missingCount > 0)
                throw new Exception(string.Format("Found {0} files that are missing from the remote storage, please run cleanup", missingCount));
		}
		
        /// <summary>
        /// Helper method that verifies uploaded volumes and updates their state in the database.
        /// Throws an error if there are issues with the remote storage
        /// </summary>
        /// <param name="backend">The backend instance to use</param>
        /// <param name="options">The options used</param>
        /// <param name="database">The database to compare with</param>
        public static Tuple<IEnumerable<IFileEntry>, IEnumerable<RemoteVolumeEntry>> RemoteListAnalysis(FhBackend backend, Options options, LocalDatabase database)
        {
            var remotelist = backend.List();
            var lookup = new Dictionary<string, Library.Interface.IFileEntry>();

            var prefix = options.BackupPrefix + "-";
            var suffix = "." + options.CompressionModule;
            if (!options.NoEncryption)
                suffix += "." + options.EncryptionModule;

            foreach (var s in remotelist)
            {
                if (s.Name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase) && s.Name.EndsWith(suffix, StringComparison.InvariantCultureIgnoreCase))
                    lookup[s.Name] = s;
            }

            var missing = new List<KeyValuePair<RemoteVolumeEntry, IFileEntry>>();
            var locallist = database.GetRemoteVolumes();
            foreach (var i in locallist)
            {
                if (i.State == RemoteVolumeState.Temporary)
                {
                    database.LogMessage("info", string.Format("removing file listed as {0}: {1}", i.State, i.Name), null);
                    database.RemoveRemoteVolume(i.Name);
                }
                else
                {
                    Library.Interface.IFileEntry r;
                    if (!lookup.TryGetValue(i.Name, out r) || (r.Size != i.Size && r.Size >= 0 && i.Size >= 0))
                    {
                        if (i.State == RemoteVolumeState.Uploading)
                        {
                            database.LogMessage("info", string.Format("removing file listed as {0}: {1}", i.State, i.Name), null);
                            database.RemoveRemoteVolume(i.Name);
                        }
                        else
                            missing.Add(new KeyValuePair<RemoteVolumeEntry, Library.Interface.IFileEntry>(i, r));
                    }
                    else if (i.State != RemoteVolumeState.Verified)
                    {
                        database.UpdateRemoteVolume(i.Name, RemoteVolumeState.Verified, i.Size, i.Hash);
                    }

                    lookup.Remove(i.Name);
                }
            }
            
            return new Tuple<IEnumerable<IFileEntry>, IEnumerable<RemoteVolumeEntry>> (lookup.Values, from n in missing select n.Key);
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
            using (var b = new FhBackend(target, opts, db, stat))
                return
                    from n in b.List()
                    let np = Volumes.VolumeBase.ParseFilename(n)
                    where np != null
                    select np;
        }

        internal static string CompactBlocks(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            using(var h = new Operation.CompactHandler(target, new FhOptions(options), stat))
            	return h.Run();
        }

        internal static string RecreateDatabase(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
        	var opts = new FhOptions(options);
        	if (System.IO.File.Exists(opts.Fhdbpath))
        		throw new Exception("The database already exists!");
        		
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

    }
}
