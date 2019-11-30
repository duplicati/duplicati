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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Public interface for an encryption method.
    /// All modules that implements encryption must implement this interface.
    /// The classes that implements this interface MUST also 
    /// implement a default constructor and a constructor that
    /// has the signature new(string passphrase, Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// An instance can be used to encrypt or decrypt multiple files/streams.
    /// </summary>
    public interface IEncryption : IDisposable
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
        /// Decrypts the contents of the input stream, and writes the result to the output stream.
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
        /// <param name="input">The target stream</param>
        /// <returns>An encrypted stream that can be written to</returns>
        Stream Encrypt(Stream input);

        /// <summary>
        /// The extension that the encryption implementation adds to the filename
        /// </summary>
        string FilenameExtension { get; }

        /// <summary>
        /// A localized string describing the encryption module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the encryption module
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }

        /// <summary>
        /// Returns the size in bytes of the overhead that will be added to a file of the given size when encrypted
        /// </summary>
        /// <param name="filesize">The size of the file to encrypt</param>
        /// <returns>The size of the overhead in bytes</returns>
        long SizeOverhead(long filesize);
    }
}
