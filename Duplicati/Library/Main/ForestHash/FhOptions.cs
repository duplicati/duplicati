using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.ForestHash
{
    public class FhOptions : Options
    {
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
