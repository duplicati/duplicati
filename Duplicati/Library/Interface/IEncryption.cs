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
    public interface IEncryption : IDynamicModule, IDisposable
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
        /// Returns the size in bytes of the overhead that will be added to a file of the given size when encrypted
        /// </summary>
        /// <param name="filesize">The size of the file to encrypt</param>
        /// <returns>The size of the overhead in bytes</returns>
        long SizeOverhead(long filesize);
    }
}
