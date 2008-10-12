using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Encryption
{
    /// <summary>
    /// Implements a encryption/decryption with the GNU Privacy Guard (GPG)
    /// </summary>
    public class GPGEncryption : EncryptionBase, IEncryption
    {
        /// <summary>
        /// The PGP program to use, should be with absolute path
        /// </summary>
        public static string PGP_PROGRAM = "pgp";

        /// <summary>
        /// Always present commandline args
        /// </summary>
        private const string COMMANDLINE_ARGS = "--symmetric --armor";

        /// <summary>
        /// Commandline switches for encryption
        /// </summary>
        private const string ENCRYPTION_ARGS = "--encrypt";

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

        public override void Encrypt(System.IO.Stream input, System.IO.Stream output)
        {
            this.Execute(COMMANDLINE_ARGS + " " + ENCRYPTION_ARGS, input, output);
        }

        public override void Decrypt(System.IO.Stream input, System.IO.Stream output)
        {
            this.Execute(COMMANDLINE_ARGS + " " + DECRYPTION_ARGS, input, output);
        }

        /// <summary>
        /// Internal helper that wraps GPG usage
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <param name="input">The input stream</param>
        /// <param name="output">The output stream</param>
        private void Execute(string args, System.IO.Stream input, System.IO.Stream output)
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

            System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
            p.StandardInput.WriteLine(m_key);

            //Prevent blocking of the output buffer
            System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(Runner));
            t.Start(new object[] {p.StandardOutput.BaseStream, output});

            //Copy input stream to GPG
            Core.Utility.CopyStream(input, p.StandardInput.BaseStream);
            p.StandardInput.Close();

            if (!t.Join(5000))
                throw new Exception("Failure while invoking GnuPG, program won't flush output");

            if (!p.WaitForExit(5000))
                throw new Exception("Failure while invoking GnuPG, program won't terminate");
        }

        private void Runner(object x)
        {
            //Unwrap arguments and read stream
            object[] tmp = (object[])x;
            Core.Utility.CopyStream((Stream)tmp[0], (Stream)tmp[1]);
        }

        #endregion
    }
}
