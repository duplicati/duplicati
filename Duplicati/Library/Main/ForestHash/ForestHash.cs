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
        public static void VerifyRemoteList(FhBackend backend, Options options, Localdatabase database)
        {
            var remotelist = backend.List();
            var lookup = new Dictionary<string, Library.Interface.IFileEntry>();

            var prefix = options.BackupPrefix + "-";
            var suffix = "." + options.CompressionModule;
            if (!options.NoEncryption)
                suffix += "." + options.EncryptionModule;

            foreach (var s in remotelist)
            {
                if (s.Name.StartsWith(prefix))
                    lookup[s.Name] = s;
            }

            var missing = new List<KeyValuePair<RemoteVolumeEntry, Library.Interface.IFileEntry>>();
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

            if (lookup.Count > 0)
                throw new Exception(string.Format("Found {0} remote files that are not recorded in local storage, please run cleanup", lookup.Count));

            if (missing.Count > 0)
                throw new Exception(string.Format("Found {0} files that are missing from the remote storage, please run cleanup", missing.Count));
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

            public Metahash(Dictionary<string, string> values)
            {
                m_values = values;
                var sha = System.Security.Cryptography.SHA256.Create();
                using (var ms = new System.IO.MemoryStream())
                using (var w = new StreamWriter(ms, Encoding.UTF8))
                {
                    w.Write(JsonConvert.SerializeObject(values));
                    w.Flush();

                    m_blob = ms.ToArray();

                    ms.Position = 0;
                    m_hash = Convert.ToBase64String(sha.ComputeHash(ms));
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
        public static IMetahash WrapMetadata(Dictionary<string, string> values)
        {
            return new Metahash(values);
        }




        internal static IEnumerable<Volumes.IParsedVolume> ParseFileList(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            var opts = new FhOptions(options);
            using (var db = new Localdatabase(opts.Fhdbpath, "ParseFileList"))
            using (var b = new FhBackend(target, opts, db, stat))
                return
                    from n in b.List()
                    let np = Volumes.VolumeBase.ParseFilename(n)
                    where np != null
                    select np;
        }

        internal static string CompactBlocks(string target, Dictionary<string, string> options)
        {
            throw new NotImplementedException();
        }
    }
}
