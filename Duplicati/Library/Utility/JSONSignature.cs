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
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// Implementation of the SIGJSON signature format
/// </summary>
public static class JSONSignature
{
    /// <summary>
    /// The RSA SHA256 signature method with PKCS1 padding
    /// </summary>
    public const string RSA_SHA256 = "RS256";
    /// <summary>
    /// The RSA SHA384 signature method with PKCS1 padding
    /// </summary>
    public const string RSA_SHA384 = "RS384";
    /// <summary>
    /// The RSA SHA512 signature method with PKCS1 padding
    /// </summary>
    public const string RSA_SHA512 = "RS512";

    /// <summary>
    /// Mapping of the headers to the version number
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> SIGJSONHeaders = new Dictionary<string, int>
    {
        {Convert.ToBase64String("//SIGJSONv1: "u8), 1},
        {Convert.ToBase64String("//SIGJSONv2: "u8), 2},
        {Convert.ToBase64String("//SIGJSONv3: "u8), 3},
        {Convert.ToBase64String("//SIGJSONv4: "u8), 4},
        {Convert.ToBase64String("//SIGJSONv5: "u8), 5},
        {Convert.ToBase64String("//SIGJSONv6: "u8), 6},
        {Convert.ToBase64String("//SIGJSONv7: "u8), 7},
        {Convert.ToBase64String("//SIGJSONv8: "u8), 8},
        {Convert.ToBase64String("//SIGJSONv9: "u8), 9}
    };

    /// <summary>
    /// The header type for the signature
    /// </summary>
    private const string HEADER_TYPE = "SIGJSONv1";

    /// <summary>
    /// The length of the signature header string
    /// </summary>
    private static readonly int SignatureLength = "//SIGJSONv1: ".Length;

    /// <summary>
    /// The built-in signing methods
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Stream, SignOperation, byte[]>> _builtInSigningMethods = new Dictionary<string, Func<Stream, SignOperation, byte[]>>
    {
        [RSA_SHA256] = (stream, key) => SignRSA(stream, key.PrivateKey, HashAlgorithmName.SHA256),
        [RSA_SHA384] = (stream, key) => SignRSA(stream, key.PrivateKey, HashAlgorithmName.SHA384),
        [RSA_SHA512] = (stream, key) => SignRSA(stream, key.PrivateKey, HashAlgorithmName.SHA512)
    };

    /// <summary>
    /// The built-in verification methods
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Stream, VerifyOperation, byte[], bool>> _builtInVerificationMethods = new Dictionary<string, Func<Stream, VerifyOperation, byte[], bool>>
    {
        [RSA_SHA256] = (stream, key, signature) => VerifyRSA(stream, key.PublicKey, signature, HashAlgorithmName.SHA256),
        [RSA_SHA384] = (stream, key, signature) => VerifyRSA(stream, key.PublicKey, signature, HashAlgorithmName.SHA384),
        [RSA_SHA512] = (stream, key, signature) => VerifyRSA(stream, key.PublicKey, signature, HashAlgorithmName.SHA512)
    };

    /// <summary>
    /// Signs a stream with the specified method
    /// </summary>
    /// <param name="Algorithm">The algorithm to use</param>
    /// <param name="PublicKey">The public key to validate with</param>
    /// <param name="PrivateKey">The private key, if signing</param>
    /// <param name="HeaderValues">The additional header values, if any</param>
    /// <param name="SignMethod">A custom method to sign with; Use null for the built-in methods</param>
    public record SignOperation(string Algorithm, string PublicKey, string PrivateKey, IEnumerable<KeyValuePair<string, string>>? HeaderValues = null, Func<Stream, SignOperation, byte[]>? SignMethod = null);

    /// <summary>
    /// Verifies a stream with the specified method
    /// </summary>
    /// <param name="Algorithm">The algorithm to use</param>
    /// <param name="PublicKey">The key to verify with</param>
    /// <param name="VerifyMethod">A custom method to verify with; Use null for the built-in methods</param>
    public record VerifyOperation(string Algorithm, string PublicKey, Func<Stream, VerifyOperation, byte[], bool>? VerifyMethod = null);

    /// <summary>
    /// The result of a verification with a match
    /// </summary>
    /// <param name="Algorithm">The algorithm matched</param>
    /// <param name="PublicKey">The public key matched</param>
    /// <param name="HeaderValues">The header values matched</param>
    public record VerifyMatch(string Algorithm, string PublicKey, IEnumerable<KeyValuePair<string, string>> HeaderValues);

    /// <summary>
    /// Signs a stream with SIGJSONv1 for multiple keys
    /// </summary>
    /// <param name="source">The stream of data to sign; must be seekable</param>
    /// <param name="target">The target stream where signed data is written</param>
    /// <param name="signOperations">The key setups to sign with</param>
    /// <returns>An awaitable task</returns>
    public static async Task SignAsync(Stream source, Stream target, IEnumerable<SignOperation> signOperations)
    {
        if (signOperations == null || !signOperations.Any())
            throw new InvalidOperationException("No sign operations provided, cannot sign data");

        source.Position = 0;
        var buffer = new byte["//SIGJSON".Length];
        await source.ReadAsync(buffer, 0, buffer.Length);
        if (Encoding.UTF8.GetString(buffer) == "//SIGJSON")
            throw new Exception("Cannot sign data with an existing signature");

        foreach (var key in signOperations)
        {
            source.Position = 0;
            await target.WriteAsync(Encoding.UTF8.GetBytes(CreateSignature(source, key)));
        }


        source.Position = 0;
        await source.CopyToAsync(target);
        await target.FlushAsync();
    }

    /// <summary>
    /// Validates a stream with SIGJSONv1 for at least one key match
    /// </summary>
    /// <param name="source">The stream of data to validate; must be seekable</param>
    /// <param name="verifyOperations">The verification operations to use</param>
    /// <returns><c>true</c> if at least one match is found; <c>false</c> otherwise</returns>
    /// <remarks>The stream is reset to the content position after validation</remarks>
    public static bool VerifyAtLeastOne(Stream source, IEnumerable<VerifyOperation> verifyOperations)
        => Verify(source, verifyOperations).Any();

    /// <summary>
    /// Validates a stream with SIGJSONv1 for multiple keys
    /// </summary>
    /// <param name="source">The stream of data to validate; must be seekable</param>
    /// <param name="verifyOperations">The verification operations to use</param>
    /// <returns>A list of matches</returns>
    /// <remarks>The stream is reset to the content position after validation</remarks>
    public static IEnumerable<VerifyMatch> Verify(Stream source, IEnumerable<VerifyOperation> verifyOperations)
    {
        if (verifyOperations == null || !verifyOperations.Any())
            throw new InvalidOperationException("No verify operations provided, cannot verify data");

        var buffer = new byte[8 * 1024];
        var headerLines = new List<(byte[] Header, byte[] Signature)>();

        while (true)
        {
            // Record the start, so we can return to it if the line is not a signature
            var position = source.Position;

            // Get the buffer as a span
            var line = buffer.AsSpan(0, source.Read(buffer));
            // Find the line delimiter
            var newlineIndex = Math.Max(0, line.IndexOf((byte)'\n'));
            // Get the part of the buffer that contains the line
            line = line[..newlineIndex];

            // Check if the signature matches a known version
            var version = 0;
            if (line.Length > SignatureLength)
                SIGJSONHeaders.TryGetValue(Convert.ToBase64String(line[..SignatureLength]), out version);

            // No matching header found, reset stream to include the comment line and stop searching
            if (version < 1)
            {
                source.Position = position;
                break;
            }

            // Valid signature line, set the stream to exclude the comment line
            source.Position = position + newlineIndex + 1;

            // We only parse the v1 lines for now, and ignore the others
            if (version == 1)
            {
                // This can throw on non-UTF data, non-base64 data, etc
                try
                {
                    var kp = Encoding.UTF8.GetString(line[SignatureLength..]).Split('.', StringSplitOptions.TrimEntries);
                    if (kp.Length == 2)
                        headerLines.Add((
                            Header: Convert.FromBase64String(kp[0]),
                            Signature: Convert.FromBase64String(kp[1])
                        ));
                }
                catch
                {
                    // Treat invalid lines as not a signature
                    source.Position = position;
                    break;
                }
            }
        }

        // No more header lines, so we are at the start of the content
        var startOfContent = source.Position;
        var result = new List<VerifyMatch>();

        // For each header line, validate the signature
        foreach (var (header, signature) in headerLines)
        {
            source.Position = startOfContent;
            result.AddRange(ValidateSignature(source, header, verifyOperations, signature));
        }

        // Reset the stream to the start of the content
        source.Position = startOfContent;
        return result;
    }

    /// <summary>
    /// Creates a single signature line
    /// </summary>
    /// <param name="data">The data to sign</param>
    /// <param name="skey">The sign operation data</param>
    /// <returns>The signature string</returns>
    private static string CreateSignature(Stream data, SignOperation skey)
    {
        var headers = skey.HeaderValues ?? new List<KeyValuePair<string, string>>();
        headers = headers.Append(new KeyValuePair<string, string>("alg", skey.Algorithm));
        headers = headers.Append(new KeyValuePair<string, string>("key", skey.PublicKey));
        headers = headers.Append(new KeyValuePair<string, string>("typ", HEADER_TYPE));

        if (headers.DistinctBy(x => x.Key).Count() != headers.Count())
            throw new InvalidOperationException("Duplicate headers are not allowed");

        var headerData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(headers, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var signMethod = skey.SignMethod ?? _builtInSigningMethods[skey.Algorithm];
        if (signMethod == null)
            throw new InvalidOperationException($"No signing method found for: {skey.Algorithm}");

        var intBuf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(intBuf, (uint)headerData.Length);

        using var headerStream = new MemoryStream();
        headerStream.Write(intBuf);
        headerStream.Write(headerData);

        headerStream.Position = 0;
        using var prefixedStream = new CombinedStream([headerStream, data], leaveOpen: true);
        var signature = signMethod(prefixedStream, skey);

        var sigJsonV1 = new StringBuilder();
        sigJsonV1.Append("//SIGJSONv1: ");
        sigJsonV1.Append(Convert.ToBase64String(headerData));
        sigJsonV1.Append(".");
        sigJsonV1.Append(Convert.ToBase64String(signature));
        sigJsonV1.Append("\n");

        return sigJsonV1.ToString();
    }

    /// <summary>
    /// Verifies a single signature line
    /// </summary>
    /// <param name="data">The payload data</param>
    /// <param name="header">The header data</param>
    /// <param name="verifiers">The verification operations possible</param>
    /// <param name="signature">The signature to match</param>
    /// <returns>A list of matches</returns>
    private static IEnumerable<VerifyMatch> ValidateSignature(Stream data, byte[] header, IEnumerable<VerifyOperation> verifiers, byte[] signature)
    {
        var startOfContent = data.Position;

        List<KeyValuePair<string, string>>? headers = null;
        try { headers = System.Text.Json.JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(header); }
        catch { }

        var alg = headers?.FirstOrDefault(x => x.Key == "alg").Value;
        var key = headers?.FirstOrDefault(x => x.Key == "key").Value;
        var typ = headers?.FirstOrDefault(x => x.Key == "typ").Value;
        if (string.IsNullOrWhiteSpace(alg) || string.IsNullOrWhiteSpace(key) || typ != HEADER_TYPE)
            yield break;

        var potentialMatches = verifiers.Where(x => x.Algorithm == alg && x.PublicKey == key);
        if (!potentialMatches.Any())
            yield break;

        var intBuf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(intBuf, (uint)header.Length);

        using var headerStream = new MemoryStream();
        headerStream.Write(intBuf);
        headerStream.Write(header);

        foreach (var verifier in potentialMatches)
        {
            var verifyMethod = verifier.VerifyMethod ?? _builtInVerificationMethods[verifier.Algorithm];
            if (verifyMethod == null)
                throw new InvalidOperationException($"No verification method found for: {verifier.Algorithm}");

            headerStream.Position = 0;
            data.Position = startOfContent;

            using var prefixedStream = new CombinedStream([headerStream, data], leaveOpen: true);
            if (verifyMethod(prefixedStream, verifier, signature))
                yield return new VerifyMatch(alg, key, headers!);
        }
    }

    /// <summary>
    /// Signs a stream with RSA and the specified hash algorithm
    /// </summary>
    /// <param name="data">The data to sign</param>
    /// <param name="privateKey">The key to sign with</param>
    /// <param name="hashAlgorithmName">The hash algorithm to use</param>
    /// <returns>The signature</returns>
    private static byte[] SignRSA(Stream data, string privateKey, HashAlgorithmName hashAlgorithmName)
    {
        using var rsa = new RSACryptoServiceProvider();
        rsa.FromXmlString(privateKey);
        return rsa.SignData(data, hashAlgorithmName, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// Verifies a stream signed with RSA and the specified hash algorithm
    /// </summary>
    /// <param name="data">The data to verify</param>
    /// <param name="publicKey">The key to verify with</param>
    /// <param name="signature">The signature to use</param>
    /// <param name="hashAlgorithmName">The algorithm to use</param>
    /// <returns><c>true</c> if the signature is valid; <c>false</c> otherwise</returns>
    private static bool VerifyRSA(Stream data, string publicKey, byte[] signature, HashAlgorithmName hashAlgorithmName)
    {
        using var rsa = new RSACryptoServiceProvider();
        rsa.FromXmlString(publicKey);
        return rsa.VerifyData(data, signature, hashAlgorithmName, RSASignaturePadding.Pkcs1);
    }
}