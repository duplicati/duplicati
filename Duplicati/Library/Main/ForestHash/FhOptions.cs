using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.ForestHash
{
    public class FhOptions : Options
    {
        private const string DEFAULT_BLOCK_HASH_LOOKUP_SIZE = "64mb";
        private const string DEFAULT_METADATA_HASH_LOOKUP_SIZE = "64mb";
        private const string DEFAULT_FILE_HASH_LOOKUP_SIZE = "32mb";
		private const string DEFAULT_FILENAME_LOOKUP_SIZE = "64mb";
		
		private const string DEFAULT_BLOCK_HASH_ALGORITHM = "SHA256";
		private const string DEFAULT_FILE_HASH_ALGORITHM = "SHA256";
		
        /// <summary>
        /// The default block size for Foresthash
        /// </summary>
        private const string DEFAULT_FH_BLOCKSIZE = "100kb";

        public FhOptions(Dictionary<string, string> options)
            : base(options)
        {
        }

		private static string[] GetSupportedHashes()
		{
			var r = new List<string>();
			foreach(var h in new string[] {"SHA1", "MD5", "SHA256", "SHA384", "SHA512"})
			try 
			{
				var p = System.Security.Cryptography.HashAlgorithm.Create(h);
				if (p != null)
					r.Add(h);
			}
			catch
			{
			}
			
			return r.ToArray();
		}

        /// <summary>
        /// List of commands relating to the ForestHash module
        /// </summary>
        public static IList<ICommandLineArgument> Commands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("fh-dbpath", CommandLineArgument.ArgumentType.Path, Strings.FhOptions.FhdbpathShort, Strings.FhOptions.FhdbpathLong),
                    new CommandLineArgument("fh-blocksize", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhblocksizeShort, Strings.FhOptions.FhblocksizeLong, DEFAULT_FH_BLOCKSIZE),
                    new CommandLineArgument("fh-no-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhnometadataShort, Strings.FhOptions.FhnometadataLong, "false"),
                    new CommandLineArgument("fh-blockhash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhblockhashlookupsizeShort, Strings.FhOptions.FhblockhashlookupsizeLong, DEFAULT_BLOCK_HASH_LOOKUP_SIZE),
                    new CommandLineArgument("fh-filehash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhfilehashlookupsizeShort, Strings.FhOptions.FhfilehashlookupsizeLong, DEFAULT_FILE_HASH_LOOKUP_SIZE),
                    new CommandLineArgument("fh-metadatahash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhmetadatahashlookupsizeShort, Strings.FhOptions.FhmetadatahashlookupsizeLong, DEFAULT_METADATA_HASH_LOOKUP_SIZE),
					new CommandLineArgument("fh-filepath-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhfilepathlookupsizeShort, Strings.FhOptions.FhfilepathlookupsizeLong, DEFAULT_FILENAME_LOOKUP_SIZE),
                    new CommandLineArgument("fh-changed-fileset", CommandLineArgument.ArgumentType.Path, Strings.FhOptions.FhchangedfilesetShort, Strings.FhOptions.FhchangedfilesetLong),
                    new CommandLineArgument("fh-deleted-fileset", CommandLineArgument.ArgumentType.Path, Strings.FhOptions.FhdeletedfilesetShort, string.Format(Strings.FhOptions.FhdeletedfilesetLong, "fh-changed-fileset")),

                    new CommandLineArgument("fh-max-wastesize", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhmaxwastesizeShort, Strings.FhOptions.FhmaxwastesizeLong),
                    new CommandLineArgument("fh-no-indexfiles", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhnoindexfilesShort, Strings.FhOptions.FhnoindexfilesLong, "false"),
                    new CommandLineArgument("fh-no-backendverification", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhnobackendverificationShort, Strings.FhOptions.FhnobackendverificationLong, "false"),
                    new CommandLineArgument("fh-dry-run", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhdryrunShort, Strings.FhOptions.FhdryrunLong, "false"),

                    new CommandLineArgument("fh-block-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.FhOptions.FhblockhashalgorithmShort, Strings.FhOptions.FhblockhashalgorithmLong, DEFAULT_BLOCK_HASH_ALGORITHM, null, GetSupportedHashes()),
                    new CommandLineArgument("fh-file-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.FhOptions.FhfilehashalgorithmShort, Strings.FhOptions.FhfilehashalgorithmLong, DEFAULT_FILE_HASH_ALGORITHM, null, GetSupportedHashes()),

                    new CommandLineArgument("fh-no-auto-compact", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhnoautocompactShort, Strings.FhOptions.FhnoautocompactLong, "false"),
                    new CommandLineArgument("fh-volume-size-tolerance", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhvolumesizetoleranceShort, Strings.FhOptions.FhvolumesizetoleranceLong),

                    new CommandLineArgument("fh-patch-with-local-blocks", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhpatchwithlocalblocksShort, Strings.FhOptions.FhpatchwithlocalblocksLong),
#if DEBUG
                    new CommandLineArgument("fh-no-local-blocks", CommandLineArgument.ArgumentType.Boolean, "Prevents using local blocks for restore", "", "false"),
                    new CommandLineArgument("fh-no-local-db", CommandLineArgument.ArgumentType.Boolean, "Prevents using local database for restore", "", "false"),
#endif
                });
            }
        }
        
        /// <summary>
        /// Gets the path to the database
        /// </summary>
        public string Fhdbpath
        {
            get
            {
                string tmp;
                m_options.TryGetValue("fh-dbpath", out tmp);
                return tmp;
            }
        }

        /// <summary>
        /// Gets the size of file-blocks
        /// </summary>
        public int Fhblocksize
        {
            get
            {
                string tmp;
                if (!m_options.TryGetValue("fh-blocksize", out tmp))
                    tmp = DEFAULT_FH_BLOCKSIZE;

                long t = Utility.Sizeparser.ParseSize(tmp, "kb");
                if (t > int.MaxValue || t < 1024)
                    throw new ArgumentOutOfRangeException("FhBlocksize", string.Format("The blocksize cannot be less than {0}, nor larger than {1}", 1024, int.MaxValue));
                
                return (int)t;
            }
        }

        /// <summary>
        /// Gets a flag indicating if metadata for files and folders should be ignored
        /// </summary>
        public bool FhNoMetadata
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-metadata"); }
        }

        /// <summary>
        /// Gets the block hash lookup size
        /// </summary>
        public long FhBlockHashLookupMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-blockhash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_BLOCK_HASH_LOOKUP_SIZE;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public long FhFileHashLookupMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-filehash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_FILE_HASH_LOOKUP_SIZE;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the block hash size
        /// </summary>
        public long FhMetadataHashMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-metadatahash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_METADATA_HASH_LOOKUP_SIZE;
                
                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public long FhFilePathMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-filepath-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
					v = DEFAULT_FILENAME_LOOKUP_SIZE;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        
        /// <summary>
        /// Gets the maximum wasted remote size
        /// </summary>
        public long FhMaxWasteSize
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-max-wastesize", out v);
                if (string.IsNullOrEmpty(v))
					return this.VolumeSize * 2;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the volume size tolerance
        /// </summary>
        public long FhVolsizeTolerance
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-volume-size-tolerance", out v);
                if (string.IsNullOrEmpty(v))
					return this.VolumeSize / 100;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        /// <summary>
        /// List of files to check for changes
        /// </summary>
        public string[] FhChangedFilelist
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-changed-fileset", out v);
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>
        /// List of files to mark as deleted
        /// </summary>
        public string[] FhDeletedFilelist
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-deleted-fileset", out v);
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        
        /// <summary>
        /// Gets a flag indicating if index files should be omitted
        /// </summary>
        public bool FhNoIndexfiles
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-indexfiles"); }
        }
        
        /// <summary>
        /// Gets a flag indicating if the check for files on the remote storage should be omitted
        /// </summary>
        public bool FhNoBackendverification
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-backendverification"); }
        }
        
        /// <summary>
        /// Gets a flag indicating if compacting should not be done automatically
        /// </summary>
        public bool FhNoAutoCompact
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-auto-compact"); }
        }

        /// <summary>
        /// Gets a flag indicating if the current operation should merely output the changes
        /// </summary>
        public bool FhDryrun
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-dry-run"); }
        }
        
        /// <summary>
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool HasDeleteOlderThan
        {
        	get { return m_options.ContainsKey("delete-older-than"); }
        }

        /// <summary>
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool HasDeleteAllButN
        {
        	get { return m_options.ContainsKey("delete-all-but-n") || m_options.ContainsKey("delete-all-but-n-full"); }
        }

        /// <summary>
        /// The block hash algorithm to use
        /// </summary>
        public string FhBlockHashAlgorithm
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-block-hash-algorithm", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_BLOCK_HASH_ALGORITHM;

                return v;
            }
        }

        /// <summary>
        /// The file hash algorithm to use
        /// </summary>
        public string FhFileHashAlgorithm
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-file-hash-algorithm", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_FILE_HASH_ALGORITHM;

                return v;
            }
        }

        /// <summary>
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool FhPatchWithLocalBlocks
        {
        	get { return m_options.ContainsKey("fh-patch-with-local-blocks"); }
        }

#if DEBUG
        public bool NoLocalBlocks
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-local-blocks"); } 
        }
        public bool NoLocalDb
        {
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-no-local-db"); }
        }
#endif
    }
}
