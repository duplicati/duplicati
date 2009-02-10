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
using System.IO;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Public interface for an encryption method
    /// </summary>
    public interface IEncryption
    {
        /// <summary>
        /// Encrypts the contents of the inputfile, and saves the result as the outputfile.
        /// </summary>
        /// <param name="inputfile">The file to encrypt</param>
        /// <param name="outputfile">The encrypted file</param>
        void Encrypt(string inputfile, string outputfile);

        /// <summary>
        /// Encrypts the contents of the input stream, and writes the result to the output stream.
        /// </summary>
        /// <param name="input">The stream to encrypt</param>
        /// <param name="output">The encrypted stream </param>
        void Encrypt(Stream input, Stream output);

        /// <summary>
        /// Decrypts the contents of the input file and saves the result as the outputfile
        /// </summary>
        /// <param name="inputfile">The file to decrypt</param>
        /// <param name="outputfile">The decrypted output file</param>
        void Decrypt(string inputfile, string outputfile);

        /// <summary>
        /// Dencrypts the contents of the input stream, and writes the result to the output stream.
        /// </summary>
        /// <param name="input">The stream to decrypt</param>
        /// <param name="output">The decrypted stream</param>
        void Decrypt(Stream input, Stream output);

        /// <summary>
        /// Decrypts the stream to the output stream
        /// </summary>
        /// <param name="input">The encrypted stream</param>
        /// <returns>The unencrypted stream</returns>
        Stream Decrypt(Stream input);

        /// <summary>
        /// Encrypts the stream
        /// </summary>
        /// <param name="input">The unencrypted stream</param>
        /// <returns>The encrypted stream</returns>
        Stream Encrypt(Stream input);

        /// <summary>
        /// Returns the extension that the encryption implementation adds to the filename
        /// </summary>
        string FilenameExtension { get; }
    }
}
