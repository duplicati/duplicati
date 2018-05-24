#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Implements a encryption/decryption with the GNU Privacy Guard (GPG)
    /// </summary>
    public class GPGEncryption : EncryptionBase
    {
        #region Commandline option constants
        /// <summary>
        /// The commandline option supplied if armor should be disabled (--gpg-encryption-disable-armor)
        /// </summary>
        private const string COMMANDLINE_OPTIONS_DISABLE_ARMOR = "gpg-encryption-disable-armor";
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
        private readonly string m_programpath = "gpg";

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

            bool enableArmor = false;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_ENABLE_ARMOR))
            {
                enableArmor = Utility.Utility.ParseBoolOption(options, COMMANDLINE_OPTIONS_ENABLE_ARMOR);
            }
            else
            {
                //Special handling of this option, it should have been --enable-armor instead,
                //so this is now deprecated
                if (options.ContainsKey(COMMANDLINE_OPTIONS_DISABLE_ARMOR))
                    enableArmor = !Utility.Utility.ParseBoolOption(options, COMMANDLINE_OPTIONS_DISABLE_ARMOR);
            }

            if (enableArmor)
            {
                //--armor is an option
                m_encryption_args += GPG_ARMOR_OPTION;
                m_decryption_args += GPG_ARMOR_OPTION;
            }

            //--passphrase-fd is an option
            m_encryption_args += " " + GPG_COMMANDLINE_STANDARD_OPTIONS;
            m_decryption_args += " " + GPG_COMMANDLINE_STANDARD_OPTIONS;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS))
                m_encryption_args += " " + options[COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS];
            else
                m_encryption_args += " " + GPG_ENCRYPTION_DEFAULT_OPTIONS;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS))
                m_decryption_args += " " + options[COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS];
            else
                m_decryption_args += " " + GPG_DECRYPTION_DEFAULT_OPTIONS;

            //These are commands and thus inserted last

            if (options.ContainsKey(COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND))
                m_encryption_args += " " + options[COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND];
            else
                m_encryption_args += " " + GPG_ENCRYPTION_COMMAND;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_DECRYPTION_COMMAND))
                m_decryption_args += " " + options[COMMANDLINE_OPTIONS_DECRYPTION_COMMAND];
            else
                m_decryption_args += " " + GPG_DECRYPTION_COMMAND;

            if (options.ContainsKey(COMMANDLINE_OPTIONS_PATH))
                m_programpath = Library.Utility.Utility.ExpandEnvironmentVariables(options[COMMANDLINE_OPTIONS_PATH]);

        }

        #region IEncryption Members

        public override string FilenameExtension { get { return "gpg"; } }
        public override string Description { get { return Strings.GPGEncryption.Description; } }
        public override string DisplayName { get { return Strings.GPGEncryption.DisplayName; } }
        protected override void Dispose(bool disposing) { m_key = null; }

        public override System.IO.Stream Encrypt(System.IO.Stream input)
        {
            return this.Execute(m_encryption_args, input, true);
        }

        public override System.IO.Stream Decrypt(System.IO.Stream input)
        {
            return this.Execute(m_decryption_args, input, false);
        }

        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COMMANDLINE_OPTIONS_DISABLE_ARMOR, CommandLineArgument.ArgumentType.Boolean, Strings.GPGEncryption.GpgencryptiondisablearmorShort, Strings.GPGEncryption.GpgencryptiondisablearmorLong, "true", null, null, Strings.GPGEncryption.Gpgencryptiondisablearmordeprecated(COMMANDLINE_OPTIONS_ENABLE_ARMOR)),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENABLE_ARMOR, CommandLineArgument.ArgumentType.Boolean, Strings.GPGEncryption.GpgencryptionenablearmorShort, Strings.GPGEncryption.GpgencryptionenablearmorLong, "false"),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENCRYPTION_COMMAND , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptionencryptioncommandShort, Strings.GPGEncryption.GpgencryptionencryptioncommandLong(GPG_ENCRYPTION_COMMAND, "--encrypt"), GPG_ENCRYPTION_COMMAND),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_DECRYPTION_COMMAND , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptiondecryptioncommandShort, Strings.GPGEncryption.GpgencryptiondecryptioncommandLong, GPG_DECRYPTION_COMMAND),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_ENCRYPTION_OPTIONS , CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptionencryptionswitchesShort, Strings.GPGEncryption.GpgencryptionencryptionswitchesLong, GPG_ENCRYPTION_DEFAULT_OPTIONS),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_DECRYPTION_OPTIONS, CommandLineArgument.ArgumentType.String, Strings.GPGEncryption.GpgencryptiondecryptionswitchesShort, Strings.GPGEncryption.GpgencryptiondecryptionswitchesLong, GPG_DECRYPTION_DEFAULT_OPTIONS),
                    new CommandLineArgument(COMMANDLINE_OPTIONS_PATH, CommandLineArgument.ArgumentType.Path, Strings.GPGEncryption.GpgprogrampathShort, Strings.GPGEncryption.GpgprogrampathLong),
                });
            }
        }

        #endregion
        
        /// <summary>
        /// Internal helper that wraps GPG usage
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="input">The input stream</param>
        private System.IO.Stream Execute(string args, System.IO.Stream input, bool encrypt)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = args;
            psi.CreateNoWindow = true;
            psi.FileName = m_programpath;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            Console.Error.WriteLine(string.Format("Running command: \"{0}\" {1}", m_programpath, args));
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
                throw new Exception(Strings.GPGEncryption.GPGExecuteError(m_programpath, args, ex.Message), ex);
            }


            if (encrypt)
            {
                //Prevent blocking of the output buffer
                System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(Runner));
                t.Start(new object[] { p.StandardOutput.BaseStream, input });

                return new GPGStreamWrapper(p, t, p.StandardInput.BaseStream);
            }
            else
            {
                //Prevent blocking of the input buffer
                System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(Runner));
                t.Start(new object[] { input, p.StandardInput.BaseStream });

                return new GPGStreamWrapper(p, t, p.StandardOutput.BaseStream);
            }
        }

        /// <summary>
        /// Copies the content of one stream into another, invoked as a thread
        /// </summary>
        /// <param name="x">An array with two stream instances</param>
        private void Runner(object x)
        {
            //Unwrap arguments and read stream
            object[] tmp = (object[])x;
            Utility.Utility.CopyStream((Stream)tmp[0], (Stream)tmp[1]);
            ((Stream)tmp[1]).Close();
        }
    }
}
