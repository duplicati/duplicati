using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Common;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Parity
{
    public class Par2Parity : IParity
    {
        public string FilenameExtension => "par2";
        public string Description { get { return Strings.Par2Parity.Description; } }
        public string DisplayName { get { return Strings.Par2Parity.DisplayName; } }

        /// <summary>
        /// The commandline option supplied, setting the block size used for parity calculation of small file (--par2-block-size-small-file)
        /// </summary>
        private const string COMMANDLINE_BLOCK_SIZE_SF = "par2-block-size-small-file";
        /// <summary>
        /// The commandline option supplied, setting the block size used for parity calculation of large file (--par2-block-large-file)
        /// </summary>
        private const string COMMANDLINE_BLOCK_SIZE_LF = "par2-block-size-large-file";
        /// <summary>
        /// The commandline option supplied, indicating the path to the par2 program executable (--par2-program-path)
        /// </summary>
        public const string COMMANDLINE_OPTIONS_PATH = "par2-program-path";

        /// <summary>
        /// The default block size for small files (<4MB)
        /// </summary>
        private static readonly string DEFAULT_BLOCK_SIZE_SF = "5000";

        /// <summary>
        /// The default block size for large files (>4MB)
        /// </summary>
        private static readonly string DEFAULT_BLOCK_SIZE_LF = "50000";

        /// <summary>
        /// The PGP program to use, should be with absolute path
        /// </summary>
        private string m_programpath { get; set; } = GetPar2ProgramPath();

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(
                        COMMANDLINE_BLOCK_SIZE_SF,
                        CommandLineArgument.ArgumentType.Integer,
                        Strings.Par2Parity.BlocksizesmallfileShort,
                        Strings.Par2Parity.BlocksizesmallfileLong,
                        DEFAULT_BLOCK_SIZE_SF
                        ),
                    new CommandLineArgument(
                        COMMANDLINE_BLOCK_SIZE_LF,
                        CommandLineArgument.ArgumentType.Integer,
                        Strings.Par2Parity.BlocksizelargefileShort,
                        Strings.Par2Parity.BlocksizelargefileLong,
                        DEFAULT_BLOCK_SIZE_LF
                        ),
                });
            }
        }

        /// <summary>
        /// Constructs a Par2 instance for reading the interface values
        /// </summary>
        public Par2Parity() { }

        /// <summary>
        /// Constructs a new Par2 instance
        /// </summary>
        /// <param name="options">The options passed on the commandline</param>
        public Par2Parity(Dictionary<string, string> options)
        {
            if (options.ContainsKey(COMMANDLINE_OPTIONS_PATH))
                m_programpath = Environment.ExpandEnvironmentVariables(options[COMMANDLINE_OPTIONS_PATH]);
        }

        public void Create(string inputfile, string outputfile)
        {
            throw new NotImplementedException();
        }

        public void Repair(string inputfile, string parityfile, string outputfile)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines the path to the Par2 program
        /// </summary>
        public static string GetPar2ProgramPath()
        {
            Log.WriteInformationMessage("GetPar2ProgramPath", "par2", Platform.IsClientWindows ? WinTools.GetWindowsPar2ExePath() : "par2");
            // for Windows return the full path, otherwise just return "par2"
            return Platform.IsClientWindows ? WinTools.GetWindowsPar2ExePath() : "par2";
        }

        #region IDisposable Members
        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
