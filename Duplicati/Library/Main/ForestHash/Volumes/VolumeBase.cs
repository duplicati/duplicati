using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Duplicati.Library.Main.ForestHash.Volumes
{
    public abstract class VolumeBase
    {
        protected class ManifestData
        {
            private const string ENCODING = "utf8";
            private const string PRIMARYHASH = "sha256";
            private const long VERSION = 1;

            public long Version;
            public string Created;
            public string Encoding;
            public long Blocksize;
            public string PrimaryHash;

            public static string GetManifestInstance(long blocksize)
            {
                return JsonConvert.SerializeObject(new ManifestData()
                {
                    Version = VERSION,
                    Encoding = ENCODING,
                    Blocksize = blocksize,
                    Created = Utility.Utility.SerializeDateTime(DateTime.UtcNow),
                    PrimaryHash = PRIMARYHASH,
                });
            }

            public static void VerifyManifest(string manifest, long blocksize)
            {
                var d = JsonConvert.DeserializeObject<ManifestData>(manifest);
                if (d.Version > VERSION)
                    throw new InvalidManifestException("Version", d.Version.ToString(), VERSION.ToString());
                if (d.Encoding != ENCODING)
                    throw new InvalidManifestException("Encoding", d.Encoding, ENCODING);
                if (d.PrimaryHash != PRIMARYHASH)
                    throw new InvalidManifestException("PrimaryHash", d.PrimaryHash, PRIMARYHASH);
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

            private static readonly System.Text.RegularExpressions.Regex FILENAME_REGEXP = new System.Text.RegularExpressions.Regex(@"(?<prefix>[^\-]+)\-(?<filetype>(" + string.Join(")|(", Enum.GetNames(typeof(RemoteVolumeType))).ToLowerInvariant() + @"))\-(((?<guid>[0-9A-Fa-f]+)|(?<time>\d{8}T\d{6}Z)))\.(?<compression>[^\.]+)(\.(?<encryption>.+))?");

            private ParsedVolume() { }

            public static IParsedVolume Parse(string filename, Library.Interface.IFileEntry file = null)
            {
                var m = FILENAME_REGEXP.Match(filename);
                if (!m.Success || m.Length != filename.Length)
                    return null;

                return new ParsedVolume()
                {
                    Prefix = m.Groups["prefix"].Value,
                    FileType = (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), m.Groups["filetype"].Value, true),
                    Guid = m.Groups["guid"].Success ? m.Groups["guid"].Value : null,
                    Time = m.Groups["time"].Success ? DateTime.ParseExact(m.Groups["time"].Value, "yyyyMMdd'T'HHmmssK", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime() : new DateTime(0, DateTimeKind.Local),
                    CompressionModule = m.Groups["compression"].Value,
                    EncryptionModule = m.Groups["encryption"].Success ? m.Groups["encryption"].Value : null,
                    File = file
                };
            }
        }

		public static string GenerateFilename(RemoteVolumeType filetype, string prefix, string guid, DateTime timestamp, string compressionmodule, string encryptionmodule)
		{
			string volumename;
            if (filetype == RemoteVolumeType.Files)
                volumename = prefix + "-" + (filetype.ToString().ToLowerInvariant()) + "-" + Utility.Utility.SerializeDateTime(timestamp) + "." + compressionmodule;
            else
                volumename = prefix + "-" + (filetype.ToString().ToLowerInvariant()) + "-" + guid + "." + compressionmodule;

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

        protected const string SHADOW_VOLUME_FOLDER = "vol/";
        protected const string SHADOW_BLOCKLIST_FOLDER = "list/";

        protected const string CONTROL_FILES_FOLDER = "extra/";

        public static readonly System.Text.Encoding ENCODING = System.Text.Encoding.UTF8;
        protected readonly long m_blocksize;

        public VolumeBase(FhOptions options)
        {
            m_blocksize = options.Fhblocksize;
        }
    }
}
