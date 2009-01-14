using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Encryption
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
    }
}
