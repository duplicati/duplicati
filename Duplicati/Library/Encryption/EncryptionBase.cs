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
