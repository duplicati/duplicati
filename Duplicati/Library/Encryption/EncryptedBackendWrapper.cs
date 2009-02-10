#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Class that wraps a backend and transparently encrypts and decrypts data to and from the backend
    /// </summary>
    public class EncryptedBackendWrapper : Backend.IStreamingBackend
    {
        private Backend.IBackend m_realbackend;
        private IEncryption m_encryptionEngine;

        public EncryptedBackendWrapper(string target, Dictionary<string, string> options)
            : this(Backend.BackendLoader.GetBackend(target, options), options)
        {
        }

        public EncryptedBackendWrapper(Backend.IBackend backend, Dictionary<string, string> options)
        {
            if (!options.ContainsKey("passphrase") || string.IsNullOrEmpty(options["passphrase"]))
                throw new Exception("No passphrase set");

            string passphrase = options["passphrase"];

            m_realbackend = backend;
            if (options.ContainsKey("gpg-encryption"))
            {
                if (options.ContainsKey("gpg-program-path"))
                    GPGEncryption.PGP_PROGRAM = options["gpg-program-path"];
                m_encryptionEngine = new GPGEncryption(passphrase, options.ContainsKey("sign-key") ? options["sign-key"] : null);
            }
            else
                m_encryptionEngine = new AESEncryption(passphrase);
        }

        #region IStreamingBackend Members

        public string DisplayName
        {
            get { return m_realbackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return m_realbackend.ProtocolKey; }
        }

        public bool SupportsStreaming
        {
            get { return m_realbackend is Backend.IStreamingBackend ? ((Backend.IStreamingBackend)m_realbackend).SupportsStreaming : false; } 
        }

        public List<Duplicati.Library.Backend.FileEntry> List()
        {
            return m_realbackend.List();
        }

        public void Put(string remotename, System.IO.Stream stream)
        {
            if (!SupportsStreaming)
                throw new Exception("This backend does not support streaming");
            else
            {
                remotename += "." + m_encryptionEngine.FilenameExtension;
                ((Backend.IStreamingBackend)m_realbackend).Put(remotename, m_encryptionEngine.Encrypt(stream));
            }

        }

        public void Put(string remotename, string filename)
        {
            remotename += (m_encryptionEngine is AESEncryption ? ".aes" : ".gpg");
            using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
            {
                m_encryptionEngine.Encrypt(filename, tf);
                m_realbackend.Put(remotename, tf);
                tf.Protected = true;

                System.IO.File.Delete(filename);
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            if (!SupportsStreaming)
                throw new Exception("This backend does not support streaming");
            else
            {
                ((Backend.IStreamingBackend)m_realbackend).Get(remotename, m_encryptionEngine.Decrypt(stream));
            }
        }

        public void Get(string remotename, string filename)
        {
            using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
            {
                m_realbackend.Get(remotename, tf); 
                m_encryptionEngine.Decrypt(tf, filename);
            }
        }

        public void Delete(string remotename)
        {
            m_realbackend.Delete(remotename);
        }

        #endregion

        public static Backend.IBackend WrapWithEncryption(Backend.IBackend backend, Dictionary<string, string> options)
        {
            if (options.ContainsKey("no-encryption"))
                return backend;
            else
                return new EncryptedBackendWrapper(backend, options);
        }

        public static Backend.IBackend WrapWithEncryption(Backend.IBackend backend, string extension, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(extension))
                return backend;
            else if (extension == "aes")
            {
                if (options.ContainsKey("gpg-encryption"))
                    options.Remove("gpg-encryption");
            }
            else if (extension == "gpg")
                options["gpg-encryption"] = "";
            else
                throw new Exception("Unsupported encryption extension");

            return new EncryptedBackendWrapper(backend, options);
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_realbackend != null)
            {
                m_realbackend.Dispose();
                m_realbackend = null;

                m_encryptionEngine = null;
            }
        }

        #endregion

    }
}
