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
using System.Security.Cryptography;
using System.Text.Json;
using Jose;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Helper class for transport related functions
/// </summary>
internal static class TransportHelper
{
    /// <summary>
    /// Creates a signed message in JWT format using the provided private key
    /// </summary>
    /// <param name="message">The message to sign</param>
    /// <param name="privateKey">The private key to sign with</param>
    /// <returns>The signed message</returns>
    public static string CreateSignedMessage(EnvelopedMessage message, RSA privateKey)
    {
        return JWT.Encode(
            JsonSerializer.Serialize(message, options: KeepRemoteConnection.JsonOptions),
            new Jwk(privateKey, false),
            JwsAlgorithm.RS256,
            extraHeaders: new Dictionary<string, object>()
            {
                { "encrypted", "false" },
                { "version", "1" }
            }
        );
    }

    /// <summary>
    /// Creates an encrypted message in JWE format using the provided public key
    /// </summary>
    /// <param name="message">The message to encrypt</param>
    /// <param name="publicKey">The public key to encrypt with</param>
    /// <returns>The encrypted message</returns>
    public static string CreateEncryptedMessage(EnvelopedMessage message, RSA publicKey)
    {
        return JWT.Encode(
            JsonSerializer.Serialize(message, options: KeepRemoteConnection.JsonOptions),
            new Jwk(publicKey, false),
            JweAlgorithm.RSA_OAEP_256,
            JweEncryption.A256CBC_HS512,
            extraHeaders: new Dictionary<string, object>()
            {
                { "encrypted", "true" },
                { "version", "1" }
            }
        );
    }

    /// <summary>
    /// Parses a signed message using the provided public key
    /// </summary>
    /// <param name="message">The signed message to parse</param>
    /// <param name="publicKey">The public key to verify with</param>
    /// <returns>The parsed message</returns>
    public static EnvelopedMessage ParseFromSignedMessage(string message, RSA publicKey)
        => ParsedFromEncodedMessage(message, publicKey, false);

    /// <summary>
    /// Parses an encrypted message using the provided key
    /// </summary>
    /// <param name="message">The encrypted message to parse</param>
    /// <param name="privateKey">The private key to decrypt with</param>
    /// <returns>The parsed message</returns>
    public static EnvelopedMessage ParseFromEncryptedMessage(string message, RSA privateKey)
        => ParsedFromEncodedMessage(message, privateKey, true);

    /// <summary>
    /// Parses an encrypted message using the provided key
    /// </summary>
    /// <param name="message">The encrypted message to parse</param>
    /// <param name="privateKey">The private key to decrypt with</param>
    /// <returns>The parsed message</returns>
    private static EnvelopedMessage ParsedFromEncodedMessage(string message, RSA key, bool isPrivateKey)
    {
        try
        {
            return EnvelopedMessage.ForceParse(JWT.Decode(message, new Jwk(key, isPrivateKey)));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid message", ex);
        }
    }
}
