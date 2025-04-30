// Copyright (C) 2025, The Duplicati Team
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
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Implements a encryption/decryption with the GNU Privacy Guard (GPG)
    /// </summary>
    public class GPGEncryption : EncryptionBase
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<GPGEncryption>();

        #region Commandline option constants
        /// <summary>
        /// The commandline option supplied if armor should be enabled (--gpg-encryption-enable-armor)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_ENABLE_ARMOR = "gpg-encryption-enable-armor";
        /// <summary>
        /// The commandline option supplied if the encryption should have non-default switches (--gpg-encryption-switches)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS = "gpg-encryption-switches";
        /// <summary>
        /// The commandline option supplied if the decryption should have non-default switches (--gpg-decryption-switches)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS = "gpg-decryption-switches";
        /// <summary>
        /// The commandline option supplied, indicating the path to the GPG program executable (--gpg-program-path)
        /// </summary>
        public const string COMMANDLINE_OPTIONS_PATH = "gpg-program-path";
        /// <summary>
        /// The commandline option that changes the default encryption command (--gpg-encryption-command)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND = "gpg-encryption-command";
        /// <summary>
        /// The commandline option that changes the default encryption command (--gpg-decryption-command)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_DECRYPTION_COMMAND = "gpg-decryption-command";
        #endregion

        /// <summary>
        /// Always present commandline options (--passphrase-fd 0)
        /// </summary>
        private const string GPG_COMMANDLINE_STANDARD_OPTIONS = "--batch --passphrase-fd 0";

        /// <summary>
        /// The options that are supplied as default when encrypting (default is empty)
        /// </summary>
        private const string GPG_ENCRYPTION_DEFAULT_OPTIONS = "";

        /// <summary>
        /// The options that are supplied as default when encrypting (default is empty)
        /// </summary>
        private const string GPG_DECRYPTION_DEFAULT_OPTIONS = "";

        /// <summary>
        /// The commandline option that specifies that armor is used (--armor)
        /// </summary>
        private const string GPG_ARMOR_OPTION = "--armor";

        /// <summary>
        /// The command used to signal encryption (--symmetric)
        /// </summary>
        private const string GPG_ENCRYPTION_COMMAND = "--symmetric";

        /// <summary>
        /// The command used to signal decryption (--decrypt)
        /// </summary>
        private const string GPG_DECRYPTION_COMMAND = "--decrypt";

        /// <summary>
        /// The PGP program to use, should be with absolute path
        /// </summary>
        private string m_programpath { get; set; }

        /// <summary>
        /// Commandline switches for encryption
        /// </summary>
        private readonly string m_encryption_args;

        /// <summary>
        /// Commandline switches for decryption
        /// </summary>
        private readonly string m_decryption_args;

        /// <summary>
        /// The key used for cryptographic operations
        /// </summary>
        private string m_key;

        /// <summary>
        /// Constructs a GPG instance for reading the interface values
        /// </summary>
        public GPGEncryption()
        {
        }

        /// <summary>
        /// Constructs a new GPG instance
        /// </summary>
        /// <param name="options">The options passed on the commandline</param>
        /// <param name="passphrase">The passphrase to be used for encryption</param>
        public GPGEncryption(string passphrase, Dictionary<string, string> options)
        {
            m_key = passphrase;

            //NOTE: For reasons unknown, GPG commandline options are divided into "options" and "commands".
            //NOTE: The "options" must be placed before "commands" or it wont work!

            if (Utility.Utility.ParseBoolOption(options, COMMANDLINE_OPTIONS_ENABLE_ARMOR))
            {
                //--armor is an option
                m_encryption_args += GPG_ARMOR_OPTION;
                m_decryption_args += GPG_ARMOR_OPTION;
            }

            var encryptOptions = options.ContainsKey(COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS) ? options[COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS] : GPG_ENCRYPTION_DEFAULT_OPTIONS;
            var encryptCmd = options.ContainsKey(COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND) ? options[COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND] : GPG_ENCRYPTION_COMMAND;
            m_encryption_args += " " + GPG_COMMANDLINE_STANDARD_OPTIONS + " " + encryptOptions + " " + encryptCmd;

            var decryptOptions = options.ContainsKey(COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS) ? options[COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS] : GPG_DECRYPTION_DEFAULT_OPTIONS;
            var decryptCmd = options.ContainsKey(COMMANDLINE_OPTIONS_DECRYPTION_COMMAND) ? options[COMMANDLINE_OPTIONS_DECRYPTION_COMMAND] : GPG_DECRYPTION_COMMAND;
            m_decryption_args += " " + GPG_COMMANDLINE_STANDARD_OPTIONS + " " + decryptOptions + " " + decryptCmd;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_PATH))
                m_programpath = Environment.ExpandEnvironmentVariables(options[COMMANDLINE_OPTIONS_PATH]);
            else
                m_programpath = GetGpgProgramPath();

        }

        #region IEncryption Members

        public override string FilenameExtension { get { return "gpg"; } }
        public override string Description { get { return Strings.GPGEncryption.Description; } }
        public override string DisplayName { get { return Strings.GPGEncryption.DisplayName; } }
        protected override void Dispose(bool disposing) { m_key = null; }

        public override Stream Encrypt(Stream input)
        {
            return this.Execute(m_encryption_args, input, true);
        }

        public override Stream Decrypt(Stream input)
        {
            return this.Execute(m_decryption_args, input, false);
        }

        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENABLE_ARMOR, CommandLineArgument.ArgumentType.Boolean, Strings.GPGEncryption.GpgencryptionenablearmorShort, Strings.GPGEncryption.GpgencryptionenablearmorLong, "false"),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptionencryptioncommandShort, Strings.GPGEncryption.GpgencryptionencryptioncommandLong(GPG_ENCRYPTION_COMMAND, "--encrypt"), GPG_ENCRYPTION_COMMAND),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_DECRYPTION_COMMAND , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptiondecryptioncommandShort, Strings.GPGEncryption.GpgencryptiondecryptioncommandLong, GPG_DECRYPTION_COMMAND),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptionencryptionswitchesShort, Strings.GPGEncryption.GpgencryptionencryptionswitchesLong, GPG_ENCRYPTION_DEFAULT_OPTIONS),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS, CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptiondecryptionswitchesShort, Strings.GPGEncryption.GpgencryptiondecryptionswitchesLong, GPG_DECRYPTION_DEFAULT_OPTIONS),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_PATH, CommandLineArgument.ArgumentType.Path, Strings.GPGEncryption.GpgprogrampathShort, Strings.GPGEncryption.GpgprogrampathLong, GetGpgProgramPath()),
                });
            }
        }

        #endregion

        /// <summary>
        /// Internal helper that wraps GPG usage
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="input">The input stream</param>
        private Stream Execute(string args, Stream input, bool encrypt)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                Arguments = args,
                CreateNoWindow = true,
                FileName = m_programpath,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            Console.Error.WriteLine("Running command: {0} {1}", m_programpath, args);
#endif

            System.Diagnostics.Process p;

            try
            {
                p = System.Diagnostics.Process.Start(psi);
                p.StandardInput.WriteLine(m_key);
                p.StandardInput.Flush();

                System.Threading.Thread.Sleep(1000);
                if (p.HasExited)
                    throw new Exception(p.StandardError.ReadToEnd());
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "GPGEncryptFailure", ex, "Error occurred while encrypting with GPG:" + ex.Message);
                throw new Exception(Strings.GPGEncryption.GPGExecuteError(m_programpath, args, ex.Message), ex);
            }

            System.Threading.Thread t;
            if (encrypt)
            {
                //Prevent blocking of the output buffer
                t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(Runner));
                t.Start(new Stream[] { p.StandardOutput.BaseStream, input });

                return new GPGStreamWrapper(p, t, p.StandardInput.BaseStream);
            }

            //Prevent blocking of the input buffer
            t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(Runner));
            t.Start(new Stream[] { input, p.StandardInput.BaseStream });

            return new GPGStreamWrapper(p, t, p.StandardOutput.BaseStream);
        }

        /// <summary>
        /// Copies the content of one stream into another, invoked as a thread
        /// </summary>
        /// <param name="x">An array with two stream instances</param>
        private void Runner(object x)
        {
            //Unwrap arguments and read stream
            var tmp = (Stream[])x;
            Utility.Utility.CopyStream(tmp[0], tmp[1]);
            tmp[1].Close();
        }

        /// <summary>
        /// Determines the path to the GPG program
        /// </summary>
        private static string GetGpgProgramPath()
        {
            var gpgpath = GPGLocator.GetGpgExecutablePath();
            Log.WriteVerboseMessage("GetGpgProgramPath", "gpg", gpgpath);
            return gpgpath;
        }
    }
}
