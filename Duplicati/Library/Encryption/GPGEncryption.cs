#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Implements a encryption/decryption with the GNU Privacy Guard (GPG)
    /// </summary>
    public class GPGEncryption : EncryptionBase, IEncryption
    {
        /// <summary>
        /// The PGP program to use, should be with absolute path
        /// </summary>
        public static string PGP_PROGRAM = "gpg";

        /// <summary>
        /// Always present commandline args
        /// </summary>
        private const string COMMANDLINE_ARGS = "--armor --passphrase-fd 0";

        /// <summary>
        /// Commandline switches for encryption
        /// </summary>
        private const string ENCRYPTION_ARGS = "--symmetric --local-user --encrypt";

        /// <summary>
        /// Commandline switches for decryption
        /// </summary>
        private const string DECRYPTION_ARGS = "--decrypt";

        /// <summary>
        /// The key used for cryptographic operations
        /// </summary>
        private string m_key;

        /// <summary>
        /// An optional key, used for signature verification
        /// </summary>
        private string m_signaturekey;

        /// <summary>
        /// Constructs a new instance of the PGP encryption/decryption class.
        /// </summary>
        /// <param name="key">The key used to encrypt/decrypt data</param>
        /// <param name="signaturekey">The key used to generate a signature with (currently unused)</param>
        public GPGEncryption(string key, string signaturekey)
        {
            m_key = key;
            m_signaturekey = signaturekey;
        }

        #region IEncryption Members

        public override string FilenameExtension { get { return "gpg"; } }

        public override System.IO.Stream Encrypt(System.IO.Stream input)
        {
            return this.Execute(COMMANDLINE_ARGS + " " + ENCRYPTION_ARGS, input, true);
        }

        public override System.IO.Stream Decrypt(System.IO.Stream input)
        {
            return this.Execute(COMMANDLINE_ARGS + " " + DECRYPTION_ARGS, input, false);
        }

        /// <summary>
        /// Internal helper that wraps GPG usage
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="input">The input stream</param>
        /// <param name="output">The output stream</param>
        private System.IO.Stream Execute(string args, System.IO.Stream input, bool encrypt)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = args;
            psi.CreateNoWindow = true;
            psi.FileName = PGP_PROGRAM;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
#endif

            System.Diagnostics.Process p;

            try
            {
                p = System.Diagnostics.Process.Start(psi);
                p.StandardInput.WriteLine(m_key);
                p.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format(Strings.GPGEncryption.GPGExecuteError, PGP_PROGRAM, ex.Message), ex);
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

        private void Runner(object x)
        {
            //Unwrap arguments and read stream
            object[] tmp = (object[])x;
            Core.Utility.CopyStream((Stream)tmp[0], (Stream)tmp[1]);
            ((Stream)tmp[1]).Close();
        }

        #endregion
    }
}
