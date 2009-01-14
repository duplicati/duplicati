#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
