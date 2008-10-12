using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Encryption
{
    /// <summary>
    /// Simple helper class that implements the filebased functions, and wraps them onto the stream based ones
    /// </summary>
    public abstract class EncryptionBase : IEncryption
    {
        #region IEncryption Members

        public virtual void Encrypt(string inputfile, string outputfile)
        {
            using (System.IO.FileStream fs1 = System.IO.File.OpenRead(inputfile))
            using (System.IO.FileStream fs2 = System.IO.File.Create(outputfile))
                this.Encrypt(fs1, fs2);
        }

        public abstract void Encrypt(System.IO.Stream input, System.IO.Stream output);

        public virtual void Decrypt(string inputfile, string outputfile)
        {
            using (System.IO.FileStream fs1 = System.IO.File.OpenRead(inputfile))
            using (System.IO.FileStream fs2 = System.IO.File.Create(outputfile))
                this.Decrypt(fs1, fs2);
        }

        public abstract void Decrypt(System.IO.Stream input, System.IO.Stream output);

        #endregion
    }
}
