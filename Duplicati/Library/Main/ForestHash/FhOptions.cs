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

        /// <summary>
        /// The default block size for Foresthash
        /// </summary>
        private const string DEFAULT_FH_BLOCKSIZE = "10kb";

        public FhOptions(Dictionary<string, string> options)
            : base(options)
        {
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
                    new CommandLineArgument("fh-nometadata", CommandLineArgument.ArgumentType.Boolean, Strings.FhOptions.FhnometadataShort, Strings.FhOptions.FhnometadataLong, "false"),
                    new CommandLineArgument("fh-blockhash-lookup-size", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhblockhashlookupsizeShort, Strings.FhOptions.FhblockhashlookupsizeLong, DEFAULT_BLOCK_HASH_LOOKUP_SIZE),
                    new CommandLineArgument("fh-filehash-lookup-size", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhfilehashlookupsizeShort, Strings.FhOptions.FhfilehashlookupsizeLong, DEFAULT_FILE_HASH_LOOKUP_SIZE),
                    new CommandLineArgument("fh-metadatahash-lookup-size", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhmetadatahashlookupsizeShort, Strings.FhOptions.FhmetadatahashlookupsizeLong, DEFAULT_METADATA_HASH_LOOKUP_SIZE),
                    new CommandLineArgument("fh-filepath-lookup-size", CommandLineArgument.ArgumentType.Size, Strings.FhOptions.FhfilepathlookupsizeShort, Strings.FhOptions.FhfilepathlookupsizeLong, "0"),
                    new CommandLineArgument("fh-changed-fileset", CommandLineArgument.ArgumentType.Path, Strings.FhOptions.FhchangedfilesetShort, Strings.FhOptions.FhchangedfilesetLong),
                    new CommandLineArgument("fh-deleted-fileset", CommandLineArgument.ArgumentType.Path, Strings.FhOptions.FhdeletedfilesetShort, string.Format(Strings.FhOptions.FhdeletedfilesetLong, "fh-changed-fileset")),

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
        /// Gets the current block size
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
            get { return Utility.Utility.ParseBoolOption(m_options, "fh-nometadata"); }
        }

        /// <summary>
        /// Gets the block hash size
        /// </summary>
        public long FhBlockHashSize
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-blockhash-lookup-size", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_BLOCK_HASH_LOOKUP_SIZE;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public long FhFileHashSize
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-filehash-lookup-size", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_FILE_HASH_LOOKUP_SIZE;

                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the block hash size
        /// </summary>
        public long FhMetadataHashSize
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-metadatahash-lookup-size", out v);
                if (string.IsNullOrEmpty(v))
                    v = DEFAULT_METADATA_HASH_LOOKUP_SIZE;
                
                return Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public long FhFilePathSize
        {
            get
            {
                string v;
                m_options.TryGetValue("fh-filepath-lookup-size", out v);
                if (string.IsNullOrEmpty(v))
                    return 0;

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
