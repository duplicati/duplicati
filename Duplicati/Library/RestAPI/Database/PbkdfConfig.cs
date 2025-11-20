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
using System.Security.Cryptography;

#nullable enable

namespace Duplicati.Server;

/// <summary>
/// This class is used to store the PBKDF configuration parameters
/// </summary>        
public record PbkdfConfig(string Algorithm, int Version, string Salt, int Iterations, string HashAlorithm, string Hash)
{
    /// <summary>
    /// The version to embed in the configuration
    /// </summary>
    private const int _Version = 1;
    /// <summary>
    /// The algorithm to use
    /// </summary>
    private const string _Algorithm = "PBKDF2";
    /// <summary>
    /// The hash algorithm to use
    /// </summary>
    private const string _HashAlorithm = "SHA256";
    /// <summary>
    /// The number of iterations to use
    /// </summary>
    private const int _Iterations = 10000;
    /// <summary>
    /// The size of the hash
    /// </summary>
    private const int _HashSize = 32;

    /// <summary>
    /// Creates a default PBKDF2 configuration
    /// </summary>
    public static PbkdfConfig Default => new PbkdfConfig(_Algorithm, _Version, string.Empty, _Iterations, _HashAlorithm, string.Empty);

    /// <summary>
    /// Creates a new PBKDF2 configuration with a random salt
    /// </summary>
    /// <param name="password">The password to hash</param>
    public static PbkdfConfig CreatePBKDF2(string password)
    {
        var prng = RandomNumberGenerator.Create();
        var buf = new byte[_HashSize];
        prng.GetBytes(buf);

        var salt = Convert.ToBase64String(buf);
        var pwd = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, buf, _Iterations, new HashAlgorithmName(_HashAlorithm), _HashSize));

        return new PbkdfConfig(_Algorithm, _Version, salt, _Iterations, _HashAlorithm, pwd);
    }

    /// <summary>
    /// Calculates the hash for the given password using the current configuration
    /// </summary>
    /// <param name="password">The password to hash</param>
    /// <returns>The hashed password</returns>
    private string ComputeHash(string password)
    {
        return Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(Salt), Iterations, new HashAlgorithmName(HashAlorithm), _HashSize));
    }

    /// <summary>
    /// Creates a new PBKDF2 configuration with the given password
    /// </summary>
    /// <param name="password">The password to use</param>
    /// <returns>The updated PBKDF2 configuration</returns>
    public PbkdfConfig WithPassword(string password)
        => WithHash(ComputeHash(password));

    /// <summary>
    /// Creates a new PBKDF2 configuration with the given hash
    /// </summary>
    /// <param name="hash">The hash to use</param>
    /// <returns>The updated PBKDF2 configuration</returns>
    public PbkdfConfig WithHash(string hash)
        => this with { Hash = hash };

    /// <summary>
    /// Verifies a password against a PBKDF2 configuration
    /// </summary>
    /// <param name="password">The password to verify</param>
    /// <returns>True if the password matches the configuration</returns>
    public bool VerifyPassword(string password)
        => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Hash), Convert.FromBase64String(ComputeHash(password)));
}
