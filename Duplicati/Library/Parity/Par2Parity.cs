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
    /// <summary>
    /// Possible return code defined in par2cmdline.
    /// </summary>
    /// <see cref="https://github.com/Parchive/par2cmdline/blob/master/src/libpar2.h#L109-L136"/>
    public enum Par2CommandlineResult
    {
        eSuccess = 0,

        eRepairPossible = 1,  // Data files are damaged and there is
                              // enough recovery data available to
                              // repair them.

        eRepairNotPossible = 2,  // Data files are damaged and there is
                                 // insufficient recovery data available
                                 // to be able to repair them.

        eInvalidCommandLineArguments = 3,  // There was something wrong with the
                                           // command line arguments

        eInsufficientCriticalData = 4,  // The PAR2 files did not contain sufficient
                                        // information about the data files to be able
                                        // to verify them.

        eRepairFailed = 5,  // Repair completed but the data files
                            // still appear to be damaged.


        eFileIOError = 6,  // An error occurred when accessing files
        eLogicError = 7,   // In internal error occurred
        eMemoryError = 8,  // Out of memory
    }

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

#if DEBUG
        const string m_verbose_level = "-q";
#else
        const string m_verbose_level = "-qq";
#endif

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

        protected string ParseErrorCodeMessage(int retcode)
        {
            if (!Enum.IsDefined(typeof(Par2CommandlineResult), retcode))
                return "";

            var result = (Par2CommandlineResult)retcode;
            switch(result)
            {
                default:
                    return "";
                case Par2CommandlineResult.eRepairPossible:
                    return "There was something wrong with the command line arguments";
                case Par2CommandlineResult.eRepairNotPossible:
                case Par2CommandlineResult.eInsufficientCriticalData:
                    return "Insufficient information contained for repairing";
                case Par2CommandlineResult.eRepairFailed:
                    return "Repair completed but the data file still appears to be damaged.";
                case Par2CommandlineResult.eFileIOError:
                    return "An error occurred when accessing files";
                case Par2CommandlineResult.eLogicError:
                    return "Internal error occurred";
                case Par2CommandlineResult.eMemoryError:
                    return "Out of memory";
            }
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
            var args = $@"create {m_verbose_level} -r{m_parity_redundancy} -s{blocksize} -n1 ""{inputname + ".par2"}"" ""{inputname}""";

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
            Console.Error.WriteLine("Running PAR2 command: {0} {1}", m_programpath, args);
#endif

            try
            {
                Log.WriteProfilingMessage(LOGTAG, "CreatePar2File", $"Command: {m_programpath} {args}");
                using (proc = Process.Start(psi))
                {
                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                        throw new Exception($"Failed to create parity file for {inputname}. Explanation: " + ParseErrorCodeMessage(proc.ExitCode));
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "CreatePar2File", ex, "Error occurred while creating parity file with Par2:" + ex.Message);
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

        public bool Repair(string inputfile, string parityfile, out string repairedname, string outputfile = null)
        {
            using (var workdir = new TempFolder()) // make sure we have a clean workspace to find the repaired file
            {
                // Move input to working directory
                if (string.IsNullOrEmpty(outputfile))
                    outputfile = inputfile;
                var inputname = Path.GetFileName(inputfile);
                var parityname = Path.GetFileName(parityfile);
                var movedinput = Path.Combine(workdir, inputname);
                var movedparity = Path.Combine(workdir, parityname) + ".par2"; // ensure it ends with .par2 suffix
                File.Move(inputfile, movedinput);
                File.Move(parityfile, movedparity);

                // Start PAR2 process
                Process proc;
                var args = $@"repair {m_verbose_level} ""{parityname}"" ""{inputname}""";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    FileName = m_programpath,
                    Arguments = args,
                    WorkingDirectory = workdir
                };

#if DEBUG
                psi.CreateNoWindow = false;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
                Console.Error.WriteLine("Running PAR2 command: {0} {1}", m_programpath, args);
#endif

                Log.WriteProfilingMessage(LOGTAG, "RepairWithPar2File", $"Command: {m_programpath} {args}");
                try
                {
                    using (proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                        if (proc.ExitCode != 0)
                            throw new Exception($"Par2 returned {proc.ExitCode}: {ParseErrorCodeMessage(proc.ExitCode)}");
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RepairWithPar2File", ex, $"Failed to repair {inputname}");
                    repairedname = null;
                    return false;
                }

                // Move the input file back
                if (File.Exists(movedinput + ".1"))
                {
                    // The input file name is correct
                    File.Move(movedinput + ".1", inputfile);
                    File.Move(movedparity, parityfile);
                    File.Copy(movedinput, outputfile, true);
                    repairedname = inputname;
                }
                else
                {
                    // The input file name is incorrect
                    File.Move(movedinput, inputfile);
                    File.Move(movedparity, parityfile);
                    var fixedFile = Directory.GetFiles(workdir).First();
                    File.Copy(fixedFile, outputfile, true);
                    repairedname = Path.GetFileName(fixedFile);
                }
                return true;
            }
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
