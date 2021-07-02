using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Common;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Parity
{
    public class Par2Parity : IParity
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Par2Parity>();

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
        private const string COMMANDLINE_OPTIONS_PATH = "par2-program-path";

        /// <summary>
        /// The default block size for small files, use "small-file-size" option to split large and small files
        /// </summary>
        private static readonly string DEFAULT_BLOCK_SIZE_SF = "4kb";

        /// <summary>
        /// The default block size for large files
        /// </summary>
        private static readonly string DEFAULT_BLOCK_SIZE_LF = "50kb";

        /// <summary>
        /// The PGP program to use, should be with absolute path
        /// </summary>
        private string m_programpath { get; set; } = GetPar2ProgramPath();

        private readonly long m_block_size_sf;
        private readonly long m_block_size_lf;
        private readonly int m_parity_redundancy;
        private readonly long m_small_file_size;
        private TempFolder m_work_dir = new Library.Utility.TempFolder();

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
                        DEFAULT_BLOCK_SIZE_SF),
                    new CommandLineArgument(
                        COMMANDLINE_BLOCK_SIZE_LF,
                        CommandLineArgument.ArgumentType.Integer,
                        Strings.Par2Parity.BlocksizelargefileShort,
                        Strings.Par2Parity.BlocksizelargefileLong,
                        DEFAULT_BLOCK_SIZE_LF),
                    new CommandLineArgument(
                        COMMANDLINE_OPTIONS_PATH,
                        CommandLineArgument.ArgumentType.Path,
                        Strings.Par2Parity.Par2programpathShort,
                        Strings.Par2Parity.Par2programpathLong),
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
        public Par2Parity(int redundancy_level, long small_file_size, Dictionary<string, string> options)
        {
            m_parity_redundancy = redundancy_level;
            m_small_file_size = small_file_size;

            options.TryGetValue(COMMANDLINE_BLOCK_SIZE_SF, out string strBlockSizeSF);
            if (string.IsNullOrWhiteSpace(strBlockSizeSF))
                strBlockSizeSF = DEFAULT_BLOCK_SIZE_SF;
            long blockSizeSF = Library.Utility.Sizeparser.ParseSize(strBlockSizeSF);
            if (blockSizeSF % 4 != 0)
                blockSizeSF += 4 - (blockSizeSF % 4);
            m_block_size_sf = Math.Max(4, blockSizeSF);

            options.TryGetValue(COMMANDLINE_BLOCK_SIZE_LF, out string strBlockSizeLF);
            if (string.IsNullOrWhiteSpace(strBlockSizeLF))
                strBlockSizeLF = DEFAULT_BLOCK_SIZE_LF;
            long blockSizeLF = Library.Utility.Sizeparser.ParseSize(strBlockSizeLF);
            if (blockSizeLF % 4 != 0)
                blockSizeLF += 4 - (blockSizeLF % 4);
            m_block_size_lf = Math.Max(4, blockSizeLF);

            if (options.ContainsKey(COMMANDLINE_OPTIONS_PATH))
                m_programpath = Environment.ExpandEnvironmentVariables(options[COMMANDLINE_OPTIONS_PATH]);
        }

        public void Create(string inputfile, string outputfile, string inputname = null)
        {
            // Move input to working directory
            if (string.IsNullOrEmpty(inputname))
                inputname = Path.GetFileName(inputfile);
            var movedfile = Path.Combine(m_work_dir, inputname);
            File.Move(inputfile, movedfile);

            // Start PAR2 process
            Process proc;
            var blocksize = new FileInfo(movedfile).Length >= m_small_file_size ? m_block_size_lf : m_block_size_sf;
            var args = $@"create -q -r{m_parity_redundancy} -s{blocksize} -n1 ""{inputname + ".par2"}"" ""{inputname}""";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                FileName = m_programpath,
                Arguments = args,
                WorkingDirectory = m_work_dir
            };

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            Console.Error.WriteLine("Running command: {0} {1}", m_programpath, args);
#endif

            try
            {
                // Logging.Log.WriteProfilingMessage

                using (proc = Process.Start(psi))
                {
                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                        throw new Exception($"Failed to create parity file for {inputname}.");
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Par2CreateFailure", ex, "Error occurred while creating parity file with Par2:" + ex.Message);
                throw;
            }

            // Move the input file back
            File.Move(movedfile, inputfile);

            // Find the actual parity file with recovery blocks and rename it as the output file
            foreach (var filename in Directory.GetFiles(m_work_dir))
            {
                var filestem = Path.GetFileNameWithoutExtension(filename);
                if (filestem.StartsWith(inputname) && filestem.Contains("vol"))
                {
                    File.Copy(filename, outputfile, true);
                    break;
                }
            }
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
        public void Dispose()
        {
            m_work_dir.Dispose();
        }
        #endregion
    }
}
