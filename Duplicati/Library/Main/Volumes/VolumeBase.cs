using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Duplicati.Library.Main.Volumes
{
    public abstract class VolumeBase
    {
        protected class ManifestData
        {
            public const string ENCODING = "utf8";
            public const long VERSION = 2;

            public long Version;
            public string Created;
            public string Encoding;
            public long Blocksize;
            public string BlockHash;
			public string FileHash;

            public static string GetManifestInstance(long blocksize, string blockhash, string filehash)
            {
                return JsonConvert.SerializeObject(new ManifestData()
                {
                    Version = VERSION,
                    Encoding = ENCODING,
                    Blocksize = blocksize,
                    Created = Library.Utility.Utility.SerializeDateTime(DateTime.UtcNow),
                    BlockHash = blockhash,
                    FileHash = filehash
                });
            }

            public static void VerifyManifest(string manifest, long blocksize, string blockhash, string filehash)
            {
                var d = JsonConvert.DeserializeObject<ManifestData>(manifest);
                if (d.Version > VERSION)
                    throw new InvalidManifestException("Version", d.Version.ToString(), VERSION.ToString());
                if (d.Encoding != ENCODING)
                    throw new InvalidManifestException("Encoding", d.Encoding, ENCODING);
                if (d.Blocksize != blocksize)
                    throw new InvalidManifestException("Blocksize", d.Blocksize.ToString(), blocksize.ToString());
                if (d.BlockHash != blockhash)
                    throw new InvalidManifestException("BlockHash", d.BlockHash, blockhash);
                if (d.FileHash != filehash)
                    throw new InvalidManifestException("FileHash", d.FileHash, filehash);
            }
        }
        

        private class ParsedVolume : IParsedVolume
        {
            public RemoteVolumeType FileType { get; private set; }
            public string Prefix { get; private set; }
            public string Guid { get; private set; }
            public DateTime Time { get; private set; }
            public string CompressionModule { get; private set; }
            public string EncryptionModule { get; private set; }
            public Library.Interface.IFileEntry File { get; private set; }

	        internal static readonly IDictionary<RemoteVolumeType, string> REMOTE_TYPENAME_MAP;
	        internal static readonly IDictionary<string, RemoteVolumeType> REVERSE_REMOTE_TYPENAME_MAP;
            private static readonly System.Text.RegularExpressions.Regex FILENAME_REGEXP;

			static ParsedVolume()
			{
				var dict = new Dictionary<RemoteVolumeType, string>();
				dict[RemoteVolumeType.Blocks] = "dblock";
				dict[RemoteVolumeType.Files] = "dlist";
				dict[RemoteVolumeType.Index] = "dindex";
				
				var reversedict = new Dictionary<string, RemoteVolumeType>(System.StringComparer.InvariantCultureIgnoreCase);
				foreach(var x in dict)
					reversedict[x.Value] = x.Key;
								
				REMOTE_TYPENAME_MAP = dict;
				REVERSE_REMOTE_TYPENAME_MAP = reversedict;
				FILENAME_REGEXP = new System.Text.RegularExpressions.Regex(@"(?<prefix>[^\-]+)\-(([i|b|I|B](?<guid>[0-9A-Fa-f]+))|((?<time>\d{8}T\d{6}Z))).(?<filetype>(" + string.Join(")|(", dict.Values) + @"))\.(?<compression>[^\.]+)(\.(?<encryption>.+))?");
			}

            private ParsedVolume() { }

            public static IParsedVolume Parse(string filename, Library.Interface.IFileEntry file = null)
            {
                var m = FILENAME_REGEXP.Match(filename);
                if (!m.Success || m.Length != filename.Length)
                    return null;

				RemoteVolumeType t;
				if (!REVERSE_REMOTE_TYPENAME_MAP.TryGetValue(m.Groups["filetype"].Value, out t))
					return null;

                return new ParsedVolume()
                {
                    Prefix = m.Groups["prefix"].Value,
                    FileType = t,
                    Guid = m.Groups["guid"].Success ? m.Groups["guid"].Value : null,
                    Time = m.Groups["time"].Success ? Library.Utility.Utility.DeserializeDateTime(m.Groups["time"].Value).ToUniversalTime() : new DateTime(0, DateTimeKind.Utc),
                    CompressionModule = m.Groups["compression"].Value,
                    EncryptionModule = m.Groups["encryption"].Success ? m.Groups["encryption"].Value : null,
                    File = file
                };
            }
        }
        
        public static string GenerateFilename(RemoteVolumeType filetype, Options options, string guid, DateTime timestamp)
        {
            return GenerateFilename(filetype, options.Prefix, guid, timestamp, options.CompressionModule, options.NoEncryption ? null : options.EncryptionModule);
        }

		public static string GenerateFilename(RemoteVolumeType filetype, string prefix, string guid, DateTime timestamp, string compressionmodule, string encryptionmodule)
		{
			string volumename;

            if (filetype == RemoteVolumeType.Files)
                volumename = prefix + "-" + Library.Utility.Utility.SerializeDateTime(timestamp) + "." + (ParsedVolume.REMOTE_TYPENAME_MAP[filetype]) + "." + compressionmodule;
            else
                volumename = prefix + "-" + (filetype == RemoteVolumeType.Blocks ? "b" : "i") + guid + "." + (ParsedVolume.REMOTE_TYPENAME_MAP[filetype]) + "." + compressionmodule;

            if (!string.IsNullOrEmpty(encryptionmodule))
                volumename += "." + encryptionmodule;
                
            return volumename;
		}

        public static IParsedVolume ParseFilename(Library.Interface.IFileEntry file)
        {
            return ParsedVolume.Parse(file.Name, file);
        }

        public static IParsedVolume ParseFilename(string filename)
        {
            return ParsedVolume.Parse(filename);
        }

        protected const string MANIFEST_FILENAME = "manifest";
        protected const string FILELIST = "filelist.json";

        protected const string INDEX_VOLUME_FOLDER = "vol/";
        protected const string INDEX_BLOCKLIST_FOLDER = "list/";

        protected const string CONTROL_FILES_FOLDER = "extra/";

        public static readonly System.Text.Encoding ENCODING = System.Text.Encoding.UTF8;
        protected readonly long m_blocksize;
        protected readonly string m_blockhash;
        protected readonly string m_filehash;

        public VolumeBase(Options options)
        {
            m_blocksize = options.Blocksize;
            m_blockhash = options.BlockHashAlgorithm;
            m_filehash = options.FileHashAlgorithm;
        }
    }
}
