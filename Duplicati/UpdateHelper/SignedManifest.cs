// Copyright (C) 2026, The Duplicati Team
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Duplicati.UpdateHelper;

/// <summary>
/// An RSA public key as modulus and exponent, the parts carried by the RSAKeyValue XML format
/// </summary>
internal sealed class RsaPublicKey
{
    /// <summary>
    /// The modulus, big-endian
    /// </summary>
    public byte[] Modulus { get; }
    /// <summary>
    /// The public exponent, big-endian
    /// </summary>
    public byte[] Exponent { get; }

    public RsaPublicKey(byte[] modulus, byte[] exponent)
    {
        Modulus = modulus;
        Exponent = exponent;
    }

    /// <summary>
    /// Parses an <c>&lt;RSAKeyValue&gt;</c> XML string without an XML parser, to keep the trimmed helper small
    /// </summary>
    /// <param name="xml">The XML</param>
    /// <returns>The key, or null if the string is not a public key</returns>
    public static RsaPublicKey? ParseXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var modulus = ElementText(xml, "Modulus");
        var exponent = ElementText(xml, "Exponent");
        if (modulus == null || exponent == null)
            return null;

        try
        {
            return new RsaPublicKey(Convert.FromBase64String(modulus), Convert.FromBase64String(exponent));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if the keys have the same modulus and exponent
    /// </summary>
    public bool Matches(RsaPublicKey other)
        => Modulus.AsSpan().SequenceEqual(other.Modulus) && Exponent.AsSpan().SequenceEqual(other.Exponent);

    /// <summary>
    /// Creates the RSA instance for this key
    /// </summary>
    public RSA ToRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = Modulus, Exponent = Exponent });
        return rsa;
    }

    private static string? ElementText(string xml, string name)
    {
        var open = "<" + name + ">";
        var close = "</" + name + ">";
        var start = xml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += open.Length;
        var end = xml.IndexOf(close, start, StringComparison.Ordinal);
        if (end < 0)
            return null;
        return xml.Substring(start, end - start).Trim();
    }
}

/// <summary>
/// The parts of the update manifest the helper reads
/// </summary>
internal sealed class ManifestInfo
{
    public string? Version { get; set; }
    public ManifestPackage[]? Packages { get; set; }
}

/// <summary>
/// One package entry in the update manifest
/// </summary>
internal sealed class ManifestPackage
{
    public long Length { get; set; }
    public string? SHA256 { get; set; }
    public string? PackageTypeId { get; set; }
}

/// <summary>
/// One header entry of a SIGJSON signature line
/// </summary>
internal sealed class SignatureHeaderEntry
{
    public string? Key { get; set; }
    public string? Value { get; set; }
}

/// <summary>
/// Verifies update manifests signed in the SIGJSON v1 format produced by the JsonSignature package,
/// using only the BCL. A signed manifest starts with one line per signature:
/// <c>//SIGJSONv1: &lt;base64 header&gt;.&lt;base64 signature&gt;</c>, followed by the JSON document.
/// The signature is RSA PKCS#1 v1.5 over the big-endian 32-bit header length, the header bytes
/// and the document bytes.
/// </summary>
internal static class SignedManifest
{
    private const string LinePrefix = "//SIGJSONv";
    private const string Version1Prefix = "//SIGJSONv1: ";
    private const string AlgorithmRsaSha256 = "RS256";
    private const string HeaderType = "SIGJSONv1";

    /// <summary>
    /// Verifies that the manifest carries a valid signature from one of the trusted keys and returns its content
    /// </summary>
    /// <param name="text">The signed manifest text</param>
    /// <param name="trustedKeys">The keys that may sign manifests</param>
    /// <returns>The manifest</returns>
    /// <exception cref="CryptographicException">Thrown if no trusted signature verifies</exception>
    public static ManifestInfo Verify(string text, IReadOnlyList<RsaPublicKey> trustedKeys)
    {
        if (trustedKeys.Count == 0)
            throw new InvalidOperationException("No trusted signing keys are available");

        var bytes = Encoding.UTF8.GetBytes(text);
        var signatures = new List<(byte[] Header, byte[] Signature)>();
        var pos = 0;

        while (true)
        {
            var eol = Array.IndexOf(bytes, (byte)'\n', pos);
            if (eol < 0)
                break;

            var line = Encoding.UTF8.GetString(bytes, pos, eol - pos);
            if (!IsSignatureLine(line))
                break;

            if (line.StartsWith(Version1Prefix, StringComparison.Ordinal))
            {
                var parts = line.Substring(Version1Prefix.Length).Split('.', StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    try
                    {
                        signatures.Add((Convert.FromBase64String(parts[0]), Convert.FromBase64String(parts[1])));
                    }
                    catch (FormatException)
                    {
                        break;
                    }
                }
            }

            pos = eol + 1;
        }

        if (signatures.Count == 0)
            throw new CryptographicException("The manifest is not signed");

        var content = bytes.AsSpan(pos);
        var verified = false;
        foreach (var (header, signature) in signatures)
        {
            if (VerifyOne(header, signature, content, trustedKeys))
            {
                verified = true;
                break;
            }
        }

        if (!verified)
            throw new CryptographicException("The manifest has no valid signature from a trusted key");

        return JsonSerializer.Deserialize(content, HelperJsonContext.Default.ManifestInfo)
            ?? throw new InvalidOperationException("The manifest is empty");
    }

    private static bool IsSignatureLine(string line)
        => line.Length > Version1Prefix.Length
            && line.StartsWith(LinePrefix, StringComparison.Ordinal)
            && char.IsAsciiDigit(line[LinePrefix.Length])
            && line[LinePrefix.Length + 1] == ':'
            && line[LinePrefix.Length + 2] == ' ';

    private static bool VerifyOne(byte[] header, byte[] signature, ReadOnlySpan<byte> content, IReadOnlyList<RsaPublicKey> trustedKeys)
    {
        SignatureHeaderEntry[]? entries;
        try
        {
            entries = JsonSerializer.Deserialize(header, HelperJsonContext.Default.SignatureHeaderEntryArray);
        }
        catch (JsonException)
        {
            return false;
        }

        if (entries == null)
            return false;

        string? alg = null, keyXml = null, typ = null;
        foreach (var e in entries)
        {
            switch (e.Key)
            {
                case "alg": alg = e.Value; break;
                case "key": keyXml = e.Value; break;
                case "typ": typ = e.Value; break;
            }
        }

        if (alg != AlgorithmRsaSha256 || typ != HeaderType)
            return false;

        var key = RsaPublicKey.ParseXml(keyXml);
        if (key == null)
            return false;

        var trusted = false;
        foreach (var k in trustedKeys)
            if (k.Matches(key))
            {
                trusted = true;
                break;
            }

        if (!trusted)
            return false;

        var data = new byte[4 + header.Length + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)header.Length);
        header.CopyTo(data, 4);
        content.CopyTo(data.AsSpan(4 + header.Length));

        using var rsa = key.ToRsa();
        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
