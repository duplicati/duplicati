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

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SQLiteHelper;

/// <summary>
/// This class exists to support the decryption of SQLite databases that are encrypted with the RC4 algorithm.
/// This step is required as the RC4 encryption in SQLite is no longer in the free version of SQLite.
/// When the migration is no longer supported, this file can be removed.
/// </summary>
public static class SQLiteRC4Decrypter
{
    /// <summary>
    /// The SQLite magic header
    /// </summary>
    private static readonly byte[] MAGIC_HEADER = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Implements the reading of the password from env/options compatible with the previous implementation
    /// </summary>
    /// <param name="commandlineOptions">The commandline options</param>
    /// <returns>The password to use for decryption</returns>
    public static string? GetEncryptionPassword(Dictionary<string, string> commandlineOptions)
    {
        var dbPassword = Environment.GetEnvironmentVariable("DUPLICATI_DB_KEY");
        if (string.IsNullOrEmpty(dbPassword))
            dbPassword = "Duplicati_Key_42";

        // Allow override of the environment variables from the commandline
        if (commandlineOptions.ContainsKey("server-encryption-key"))
            dbPassword = commandlineOptions["server-encryption-key"];

        return dbPassword;
    }

    /// <summary>
    /// Checks if the database is encrypted by checking file header
    /// </summary>
    /// <param name="databasePath">The path to the database file</param>
    /// <returns><c>true</c> if the database is encrypted; <c>false</c> otherwise</returns>
    public static bool IsDatabaseEncrypted(string databasePath)
    {
        // A file that is not created yet is not encrypted :)
        if (!File.Exists(databasePath))
            return false;

        using (var probefs = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var probebuf = new byte[MAGIC_HEADER.Length];
            probefs.Read(probebuf, 0, probebuf.Length);
            return !MAGIC_HEADER.SequenceEqual(probebuf);
        }
    }

    /// <summary>
    /// Decrypts the SQLite database file using the provided password.
    /// </summary>
    /// <param name="databasePath">The path to the database file</param>
    /// <param name="password">The password to use for decryption</param>
    /// <param name="errorMessage">The error message if decryption failed</param>
    /// <returns><c>true</c> if the decryption was successful or the database was not encrypted; <c>false</c> otherwise</returns>
    public static void DecryptSQLiteFile(string databasePath, string password)
    {
        databasePath = Path.GetFullPath(databasePath);
        if (!IsDatabaseEncrypted(databasePath))
            return;

        // In case things fail, do not damage the original file
        using var tempfile = new TempFile();
        using (var fileStream = File.OpenRead(databasePath))
        {
            var key = SHA1.HashData(Encoding.UTF8.GetBytes(password))[..16];
            var rc4 = new RC4Engine(key);

            // Read the header and decrypt it partially
            var headerBuffer = new byte[32];
            fileStream.ReadExactly(headerBuffer, 0, headerBuffer.Length);
            rc4.ProcessBytes(headerBuffer, 0, headerBuffer.Length, headerBuffer, 0);

            // Check that decryption is correct
            if (!MAGIC_HEADER.SequenceEqual(headerBuffer[..MAGIC_HEADER.Length]))
                throw new Exception("Failed to decrypt the database header");

            int declaredPageSize = BinaryPrimitives.ReadUInt16BigEndian(headerBuffer[16..18]);
            if (declaredPageSize == 1) // 1 means 64k
                declaredPageSize = ushort.MaxValue;

            if (declaredPageSize < 512 || declaredPageSize > 65536 || declaredPageSize % 2 != 0)
                throw new Exception("Invalid page size in database header");

            // Re-decrypt the file in blocks
            fileStream.Position = 0;
            var block = new byte[declaredPageSize];

            using (var decryptedStream = File.OpenWrite(tempfile))
            {
                int bytesRead;
                while ((bytesRead = fileStream.Read(block, 0, block.Length)) > 0)
                {
                    // Stream cipher, so reset for each block
                    rc4.Reset();
                    rc4.ProcessBytes(block, 0, bytesRead, block, 0);
                    decryptedStream.Write(block, 0, bytesRead);
                }
            }
        }

        // Windows file locking can be slightly delayed
        if (OperatingSystem.IsWindows())
            System.Threading.Thread.Sleep(500);

        // Decrypt worked, place the original as a backup and move the decrypted file to the original location
        File.Move(databasePath, Path.Combine(Path.GetDirectoryName(databasePath)!, Path.GetFileNameWithoutExtension(databasePath) + $"{DateTime.Now:yyyyMMdd-HHmmss}.bak"), false);
        File.Move(tempfile, databasePath, false);
    }

    /// <summary>
    /// The bouncy castle RC4 engine, copied from https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/crypto/engines/RC4Engine.cs
    /// Modified to be stand-alone, faster, use only a single key, and not depend on bouncy castle
    /// </summary>
    private class RC4Engine
    {
        /// <summary>
        /// Length of the RC4 state
        /// </summary>
        private readonly static int STATE_LENGTH = 256;

        /// <summary>
        /// The current engine state
        /// </summary>
        private byte[] engineState;
        /// <summary>
        /// The x index
        /// </summary>
        private int x;
        /// <summary>
        /// The y index
        /// </summary>
        private int y;
        /// <summary>
        /// The precalculated initial vector
        /// </summary>
        private byte[] initialVector;

        /// <summary>
        /// Creates a new RC4 engine with the provided key
        /// </summary>
        /// <param name="key">The key to use</param>
        public RC4Engine(byte[] key)
        {
            // Precalculate the initial vector in the reset state
            initialVector = GetInitialVector(key);
            engineState = new byte[STATE_LENGTH];

            // Common reset
            Reset();
        }

        /// <summary>
        /// Processes the input data and writes the output to the output buffer
        /// </summary>
        /// <param name="input">The input buffer</param>
        /// <param name="inOff">The offset in the input buffer</param>
        /// <param name="length">The length of the input buffer</param>
        /// <param name="output">The output buffer</param>
        /// <param name="outOff">The offset in the output buffer</param>
        public void ProcessBytes(byte[] input, int inOff, int length, byte[] output, int outOff)
        {
            if (input.Length < inOff + length)
                throw new ArgumentException("input buffer too short");
            if (output.Length < outOff + length)
                throw new ArgumentException("output buffer too short");

            for (var i = 0; i < length; i++)
            {
                x = (x + 1) & 0xff;
                y = (engineState[x] + y) & 0xff;

                byte sx = engineState[x];
                byte sy = engineState[y];

                // swap
                engineState[x] = sy;
                engineState[y] = sx;

                // xor
                output[i + outOff] = (byte)(input[i + inOff] ^ engineState[(sx + sy) & 0xff]);
            }
        }

        /// <summary>
        /// Calculates the initial vector based on the key
        /// </summary>
        /// <param name="key">The key to use</param>
        /// <returns>The initial vector</returns>
        private static byte[] GetInitialVector(byte[] key)
        {
            var res = Enumerable.Range(0, STATE_LENGTH).Select(i => (byte)i).ToArray();
            int i2 = 0;

            for (int i = 0; i < res.Length; i++)
            {
                int i1 = i % key.Length;
                i2 = ((key[i1] & 0xff) + res[i] + i2) & 0xff;
                (res[i], res[i2]) = (res[i2], res[i]);
            }

            return res;
        }

        /// <summary>
        /// Resets the engine to the initial state
        /// </summary>
        public void Reset()
        {
            x = 0;
            y = 0;
            Array.Copy(initialVector, 0, engineState, 0, engineState.Length);
        }
    }
}