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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Simple helper class that implements the file-based functions, and wraps them onto the stream based ones
    /// </summary>
    public abstract class EncryptionBase : IEncryption
    {
        #region IEncryption Members

        public abstract IList<ICommandLineArgument> SupportedCommands { get; }
        public abstract string FilenameExtension { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        protected abstract void Dispose(bool disposing);

        public virtual void Encrypt(string inputfile, string outputfile)
        {
            using (System.IO.FileStream fs1 = System.IO.File.OpenRead(inputfile))
            using (System.IO.FileStream fs2 = System.IO.File.Create(outputfile))
                this.Encrypt(fs1, fs2);
        }

        public virtual void Encrypt(System.IO.Stream input, System.IO.Stream output)
        {
            using (System.IO.Stream cs = Encrypt(output))
                Utility.Utility.CopyStream(input, cs, true);
        }

        public abstract System.IO.Stream Encrypt(System.IO.Stream input);

        public virtual void Decrypt(string inputfile, string outputfile)
        {
            using (System.IO.FileStream fs1 = System.IO.File.OpenRead(inputfile))
            using (System.IO.FileStream fs2 = System.IO.File.Create(outputfile))
                this.Decrypt(fs1, fs2);
        }

        public abstract System.IO.Stream Decrypt(System.IO.Stream input);

        public virtual long SizeOverhead(long filesize)
        {
            using (Utility.TempFile t1 = new Duplicati.Library.Utility.TempFile())
            using (Utility.TempFile t2 = new Duplicati.Library.Utility.TempFile())
            {
                using (System.IO.Stream s1 = System.IO.File.Create(t1))
                {
                    long bytesleft = filesize;
                    byte[] buf = new byte[1024];
                    Random rnd = new Random();
                    while (bytesleft > 0)
                    {
                        rnd.NextBytes(buf);
                        s1.Write(buf, 0, (int)Math.Min(buf.Length, bytesleft));
                        bytesleft -= buf.Length;
                    }
                }

                Encrypt(t1, t2);

                return Math.Max(0, new System.IO.FileInfo(t2).Length - filesize);
            }
        }

        public virtual void Decrypt(System.IO.Stream input, System.IO.Stream output)
        {
            try
            {
                using (System.IO.Stream cs = Decrypt(input))
                    Utility.Utility.CopyStream(cs, output, true);
            }
            catch (System.Security.Cryptography.CryptographicException cex)
            {
                //Better error message than "Padding is invalid and cannot be removed" :)
                throw new System.Security.Cryptography.CryptographicException(Strings.EncryptionBase.DecryptionError(cex.Message), cex);
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
