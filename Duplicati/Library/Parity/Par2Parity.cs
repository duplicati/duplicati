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
using System.Threading;
using System.Threading.Tasks;
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
    public class Par2Parity : IParity
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
        /// The commandline option for the redundancy level in percent (--parity-redundancy-level).
        /// This is a par2-specific option, exposed through <see cref="SupportedCommands"/>.
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
        /// The fixed base name used for the protected file inside the working directory.
        /// Must be at least two characters: par2cmdline 0.8.1 crashes (std::out_of_range)
        /// when the protected file has a single-character base name.
        /// </summary>
        private const string CANONICAL_NAME = "data";

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
        /// Per-instance cache of the program availability probe. A single module instance
        /// lives for the duration of an operation (backup/restore), so this avoids
        /// re-probing the system for every volume while still re-probing (and re-warning)
        /// on the next operation.
        /// </summary>
        private bool? _isAvailable;

        /// <summary>
        /// Flag ensuring the "par2 not found" warning is only emitted once per instance
        /// (i.e. once per operation)
        /// </summary>
        private bool _warnedNotFound;

        /// <summary>
        /// Lock guarding the lazy availability probe
        /// </summary>
        private readonly object _availabilityLock = new();

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

            m_redundancy = Library.Utility.Utility.ParseIntOption(options, COMMANDLINE_OPTIONS_REDUNDANCY, DEFAULT_REDUNDANCY);
            if (m_redundancy <= 0)
                throw new UserInformationException(Strings.Par2Parity.Par2InvalidRedundancy(COMMANDLINE_OPTIONS_REDUNDANCY, m_redundancy), "Par2InvalidRedundancy");
        }

        #region IParity Members

        public string FilenameExtension => "par2";
        public string DisplayName => Strings.Par2Parity.DisplayName;
        public string Description => Strings.Par2Parity.Description;

        /// <summary>
        /// Determines whether the par2 program is available for use. The result is probed
        /// once per instance (i.e. once per operation); the "not found" warning is emitted
        /// at most once per instance so each operation reports it.
        /// This must only be called on a fully-initialized instance (constructed with the
        /// options constructor), never on the default-constructor metadata instance, as the
        /// program path would otherwise not be set from the options.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                lock (_availabilityLock)
                {
                    if (_isAvailable == null)
                    {
                        if (m_explicitPath)
                            _isAvailable = File.Exists(m_programpath);
                        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                            // Utility.Which is only implemented for Linux/macOS; on Windows an
                            // explicit path must be supplied (no binary is bundled for now).
                            _isAvailable = Library.Utility.Utility.Which(DEFAULT_PROGRAM);
                        else
                            _isAvailable = false;

                        if (!_isAvailable.Value && !_warnedNotFound)
                        {
                            _warnedNotFound = true;
                            Log.WriteWarningMessage(LOGTAG, "Par2NotFound", null, Strings.Par2Parity.Par2NotFound);
                        }
                    }

                    return _isAvailable.Value;
                }
            }
        }

        public async Task CreateAsync(string inputfile, string parityfile, CancellationToken cancellationToken)
        {
            using var work = new Library.Utility.TempFolder();
            var target = Path.Combine(work, CANONICAL_NAME);
            File.Copy(inputfile, target, true);

            var args = $"create -q -r{m_redundancy} -n1";
            if (!string.IsNullOrWhiteSpace(m_extraOptions))
                args += " " + m_extraOptions;
            args += $" \"{CANONICAL_NAME}.par2\" \"{CANONICAL_NAME}\"";

            await RunPar2Async(args, work, true, cancellationToken).ConfigureAwait(false);

            // par2 produces an index file plus one or more recovery files;
            // concatenate them all into a single parity file.
            var parts = Directory.GetFiles(work, CANONICAL_NAME + "*.par2");
            Array.Sort(parts, StringComparer.Ordinal);
            if (parts.Length == 0)
                throw new Exception(Strings.Par2Parity.Par2ExecuteError(m_programpath, args, "no parity files were produced"));

            using (var outfs = File.Create(parityfile))
                foreach (var p in parts)
                    using (var infs = File.OpenRead(p))
                        await infs.CopyToAsync(outfs, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> VerifyAsync(string inputfile, string parityfile, CancellationToken cancellationToken)
        {
            using var work = new Library.Utility.TempFolder();
            File.Copy(inputfile, Path.Combine(work, CANONICAL_NAME), true);
            File.Copy(parityfile, Path.Combine(work, COMBINED_NAME), true);

            return await RunPar2Async($"verify -q \"{COMBINED_NAME}\" \"{CANONICAL_NAME}\"", work, false, cancellationToken).ConfigureAwait(false) == 0;
        }

        public async Task<bool> RepairAsync(string inputfile, string parityfile, CancellationToken cancellationToken)
        {
            using var work = new Library.Utility.TempFolder();
            var target = Path.Combine(work, CANONICAL_NAME);
            File.Copy(inputfile, target, true);
            File.Copy(parityfile, Path.Combine(work, COMBINED_NAME), true);

            if (await RunPar2Async($"repair -q \"{COMBINED_NAME}\" \"{CANONICAL_NAME}\"", work, false, cancellationToken).ConfigureAwait(false) != 0)
                return false;

            // Copy the repaired file back over the input
            File.Copy(target, inputfile, true);
            return true;
        }

        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>(new ICommandLineArgument[] {
            new CommandLineArgument(COMMANDLINE_OPTIONS_PATH, CommandLineArgument.ArgumentType.Path, Strings.Par2Parity.Par2programpathShort, Strings.Par2Parity.Par2programpathLong),
            new CommandLineArgument(COMMANDLINE_OPTIONS_EXTRA, CommandLineArgument.ArgumentType.String, Strings.Par2Parity.Par2extraoptionsShort, Strings.Par2Parity.Par2extraoptionsLong),
            new CommandLineArgument(COMMANDLINE_OPTIONS_REDUNDANCY, CommandLineArgument.ArgumentType.Integer, Strings.Par2Parity.Par2redundancylevelShort, Strings.Par2Parity.Par2redundancylevelLong, DEFAULT_REDUNDANCY.ToString()),
        });

        public void Dispose() { }

        #endregion

        /// <summary>
        /// Runs the par2 program with the given arguments in the given working directory.
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="workingDir">The working directory to run par2 in</param>
        /// <param name="throwOnError">If true, a non-zero exit code throws; otherwise the exit code is returned</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The process exit code (or -1 if the process could not be started and throwOnError is false)</returns>
        private async Task<int> RunPar2Async(string args, string workingDir, bool throwOnError, CancellationToken cancellationToken)
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
                var outTask = p.StandardOutput.ReadToEndAsync(cancellationToken);
                var errTask = p.StandardError.ReadToEndAsync(cancellationToken);
                await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var stderr = await errTask.ConfigureAwait(false);
                await outTask.ConfigureAwait(false);

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
