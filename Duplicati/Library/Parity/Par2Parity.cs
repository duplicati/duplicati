// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Parity
{
    /// <summary>
    /// Implements parity/error-correction using the PAR2 standard via the
    /// external "par2" (par2cmdline) program.
    /// </summary>
    /// <remarks>
    /// par2cmdline always emits an index file plus one or more recovery volume
    /// files. To satisfy the single-parity-file contract these are concatenated
    /// into one file (the PAR2 format is a stream of self-describing packets, so
    /// a concatenation is a valid PAR2 file). par2 stores the protected file by
    /// its base name, so all operations run in a temporary working directory
    /// using a fixed canonical name, making create and repair symmetric even
    /// though Duplicati's temp files are named differently on upload and download.
    /// </remarks>
    public class Par2Parity : ParityBase
    {
        private static readonly string LOGTAG = Log.LogTagFromType<Par2Parity>();

        #region Commandline option constants
        /// <summary>
        /// The commandline option indicating the path to the par2 program executable (--par2-program-path)
        /// </summary>
        public const string COMMANDLINE_OPTIONS_PATH = "par2-program-path";
        /// <summary>
        /// The commandline option for extra options passed to par2 when creating parity (--par2-extra-options)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_EXTRA = "par2-extra-options";
        /// <summary>
        /// The (shared) commandline option for the redundancy level in percent (--parity-redundancy-level).
        /// This is registered as a general parity option, and read here from the raw options.
        /// </summary>
        private const string COMMANDLINE_OPTIONS_REDUNDANCY = "parity-redundancy-level";
        #endregion

        /// <summary>
        /// The default redundancy level in percent, if none is supplied
        /// </summary>
        private const int DEFAULT_REDUNDANCY = 5;

        /// <summary>
        /// The default name of the par2 program
        /// </summary>
        private const string DEFAULT_PROGRAM = "par2";

        /// <summary>
        /// The fixed base name used for the protected file inside the working directory
        /// </summary>
        private const string CANONICAL_NAME = "d";

        /// <summary>
        /// The name used for the combined parity file inside the working directory
        /// </summary>
        private const string COMBINED_NAME = "combined.par2";

        /// <summary>
        /// The path (or bare name) of the par2 program
        /// </summary>
        private readonly string m_programpath;

        /// <summary>
        /// True if an explicit program path was supplied
        /// </summary>
        private readonly bool m_explicitPath;

        /// <summary>
        /// The redundancy level in percent
        /// </summary>
        private readonly int m_redundancy;

        /// <summary>
        /// Any extra options supplied to par2 when creating parity
        /// </summary>
        private readonly string m_extraOptions;

        /// <summary>
        /// Process-wide cache of program availability, keyed by the resolved program path.
        /// A new module instance is created per volume, so this avoids re-probing the
        /// system (and re-warning) for every upload.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _availabilityCache = new();

        /// <summary>
        /// Flag ensuring the "par2 not found" warning is only emitted once per process
        /// </summary>
        private static int _warnedNotFound;

        /// <summary>
        /// Default constructor, used to read the filename extension and supported commands
        /// </summary>
        public Par2Parity()
        {
            m_programpath = DEFAULT_PROGRAM;
            m_extraOptions = string.Empty;
            m_redundancy = DEFAULT_REDUNDANCY;
        }

        /// <summary>
        /// Constructs a new PAR2 parity instance
        /// </summary>
        /// <param name="options">The options passed on the commandline</param>
        public Par2Parity(Dictionary<string, string> options)
        {
            if (options.TryGetValue(COMMANDLINE_OPTIONS_PATH, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                m_programpath = Environment.ExpandEnvironmentVariables(path);
                m_explicitPath = true;
            }
            else
            {
                m_programpath = DEFAULT_PROGRAM;
            }

            m_extraOptions = options.TryGetValue(COMMANDLINE_OPTIONS_EXTRA, out var extra) ? (extra ?? string.Empty) : string.Empty;

            m_redundancy = DEFAULT_REDUNDANCY;
            if (options.TryGetValue(COMMANDLINE_OPTIONS_REDUNDANCY, out var redstr) && int.TryParse(redstr, out var red))
                m_redundancy = red;
        }

        #region IParity Members

        public override string FilenameExtension => "par2";
        public override string DisplayName => Strings.Par2Parity.DisplayName;
        public override string Description => Strings.Par2Parity.Description;

        /// <summary>
        /// Determines whether the par2 program is available for use.
        /// </summary>
        public override bool IsAvailable
        {
            get
            {
                var cacheKey = (m_explicitPath ? "path:" : "which:") + m_programpath;
                var available = _availabilityCache.GetOrAdd(cacheKey, _ =>
                {
                    if (m_explicitPath)
                        return File.Exists(m_programpath);
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                        // Utility.Which is only implemented for Linux/macOS; on Windows an
                        // explicit path must be supplied (no binary is bundled for now).
                        return Library.Utility.Utility.Which(DEFAULT_PROGRAM);
                    return false;
                });

                if (!available && System.Threading.Interlocked.Exchange(ref _warnedNotFound, 1) == 0)
                    Log.WriteWarningMessage(LOGTAG, "Par2NotFound", null, Strings.Par2Parity.Par2NotFound);

                return available;
            }
        }

        public override void Create(string inputfile, string parityfile)
        {
            using var work = new Library.Utility.TempFolder();
            var target = Path.Combine(work, CANONICAL_NAME);
            File.Copy(inputfile, target, true);

            var args = $"create -q -r{m_redundancy} -n1";
            if (!string.IsNullOrWhiteSpace(m_extraOptions))
                args += " " + m_extraOptions;
            args += $" \"{CANONICAL_NAME}.par2\" \"{CANONICAL_NAME}\"";

            RunPar2(args, work, true);

            // par2 produces an index file plus one or more recovery files;
            // concatenate them all into a single parity file.
            var parts = Directory.GetFiles(work, CANONICAL_NAME + "*.par2");
            Array.Sort(parts, StringComparer.Ordinal);
            if (parts.Length == 0)
                throw new Exception(Strings.Par2Parity.Par2ExecuteError(m_programpath, args, "no parity files were produced"));

            using (var outfs = File.Create(parityfile))
                foreach (var p in parts)
                    using (var infs = File.OpenRead(p))
                        infs.CopyTo(outfs);
        }

        public override bool Verify(string inputfile, string parityfile)
        {
            using var work = new Library.Utility.TempFolder();
            File.Copy(inputfile, Path.Combine(work, CANONICAL_NAME), true);
            File.Copy(parityfile, Path.Combine(work, COMBINED_NAME), true);

            return RunPar2($"verify -q \"{COMBINED_NAME}\" \"{CANONICAL_NAME}\"", work, false) == 0;
        }

        public override bool Repair(string inputfile, string parityfile)
        {
            using var work = new Library.Utility.TempFolder();
            var target = Path.Combine(work, CANONICAL_NAME);
            File.Copy(inputfile, target, true);
            File.Copy(parityfile, Path.Combine(work, COMBINED_NAME), true);

            if (RunPar2($"repair -q \"{COMBINED_NAME}\" \"{CANONICAL_NAME}\"", work, false) != 0)
                return false;

            // Copy the repaired file back over the input
            File.Copy(target, inputfile, true);
            return true;
        }

        public override IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>(new ICommandLineArgument[] {
            new CommandLineArgument(COMMANDLINE_OPTIONS_PATH, CommandLineArgument.ArgumentType.Path, Strings.Par2Parity.Par2programpathShort, Strings.Par2Parity.Par2programpathLong),
            new CommandLineArgument(COMMANDLINE_OPTIONS_EXTRA, CommandLineArgument.ArgumentType.String, Strings.Par2Parity.Par2extraoptionsShort, Strings.Par2Parity.Par2extraoptionsLong),
        });

        protected override void Dispose(bool disposing) { }

        #endregion

        /// <summary>
        /// Runs the par2 program with the given arguments in the given working directory.
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="workingDir">The working directory to run par2 in</param>
        /// <param name="throwOnError">If true, a non-zero exit code throws; otherwise the exit code is returned</param>
        /// <returns>The process exit code (or -1 if the process could not be started and throwOnError is false)</returns>
        private int RunPar2(string args, string workingDir, bool throwOnError)
        {
            var psi = new ProcessStartInfo
            {
                FileName = m_programpath,
                Arguments = args,
                WorkingDirectory = workingDir,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using var p = Process.Start(psi) ?? throw new Exception("Unexpected failure to start par2 process");
                // Read both streams asynchronously to avoid a deadlock if a buffer fills up
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                var stderr = errTask.GetAwaiter().GetResult();
                outTask.GetAwaiter().GetResult();

                if (throwOnError && p.ExitCode != 0)
                    throw new Exception(Strings.Par2Parity.Par2ExecuteError(m_programpath, args, stderr));

                return p.ExitCode;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "Par2ExecuteFailure", ex, Strings.Par2Parity.Par2ExecuteError(m_programpath, args, ex.Message));
                if (throwOnError)
                    throw;
                return -1;
            }
        }
    }
}
