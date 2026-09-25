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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jose;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// End-to-end encryption of command payloads between the portal and this client, protocol version 2.
/// The relay server decrypts the transport layer of every message to read the envelope for routing,
/// so the command payload is encrypted separately to a key the relay does not hold, and the relay
/// forwards it unread. The envelope (from, to, type, messageId, errorMessage) stays readable by the relay.
///
/// On the wire the payload is <c>{"v":2,"jwe":"&lt;compact JWE&gt;"}</c>. A request is encrypted to this
/// client's public key and its plaintext is
/// <c>{"messageId":"...","replyKey":&lt;JWK&gt;,"request":&lt;CommandRequestMessage&gt;}</c>, where the reply key is
/// an ephemeral RSA public key the portal generated and keeps in memory. The response plaintext
/// <c>{"messageId":"...","response":&lt;CommandResponseMessage&gt;}</c> is encrypted to that reply key with the
/// same wrapper, so the portal's key is never visible to the relay. The message id inside the ciphertext binds
/// each payload to its envelope, so a relay cannot move a payload to another message or replay a response unnoticed.
///
/// Relayed payloads are base64 encoded twice (the HTTP body inside the command message, then the JWE), so either
/// side gzip compresses a plaintext above <see cref="CompressionThresholdBytes"/> before encryption and marks the
/// wrapper <c>"zip":"gzip"</c>: large requests (a restore with many paths) and large responses (a file listing)
/// are both small on the wire. The sender decides; the receiver accepts compressed and plain plaintexts alike.
/// </summary>
public static class CommandPayloadEncryption
{
    /// <summary>
    /// The wrapper version this client produces and accepts
    /// </summary>
    public const int Version = 2;

    /// <summary>
    /// The key management algorithm; the only one accepted, to rule out algorithm substitution
    /// </summary>
    public const JweAlgorithm KeyAlgorithm = JweAlgorithm.RSA_OAEP_256;

    /// <summary>
    /// The content encryption algorithm; the only one accepted
    /// </summary>
    public const JweEncryption ContentEncryption = JweEncryption.A256GCM;

    /// <summary>
    /// The key type the reply key must have
    /// </summary>
    private const string RsaKeyType = "RSA";

    /// <summary>
    /// The only compression the wrapper may name
    /// </summary>
    public const string GzipCompression = "gzip";

    /// <summary>
    /// Plaintexts larger than this are compressed before encryption; smaller ones are not worth it
    /// </summary>
    public const int CompressionThresholdBytes = 2 * 1024;

    /// <summary>
    /// The most a compressed plaintext may inflate to, so a malformed or hostile payload cannot exhaust memory
    /// </summary>
    public const int MaxDecompressedBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The wrapper placed in the envelope payload
    /// </summary>
    /// <param name="V">The wrapper version</param>
    /// <param name="Jwe">The compact JWE</param>
    /// <param name="Zip">The compression applied to the plaintext before encryption, <c>null</c> for none</param>
    public sealed record EncryptedPayload(int V, string Jwe, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Zip);

    /// <summary>
    /// The plaintext of an encrypted request
    /// </summary>
    /// <param name="MessageId">The envelope's message id, binding the request to its envelope</param>
    /// <param name="ReplyKey">The portal's public key as a JWK, to encrypt the response to</param>
    /// <param name="Request">The command request</param>
    public sealed record EncryptedRequest(string? MessageId, JsonElement ReplyKey, CommandRequestMessage Request);

    /// <summary>
    /// The plaintext of an encrypted response
    /// </summary>
    /// <param name="MessageId">The envelope's message id, binding the response to the request it answers</param>
    /// <param name="Response">The command response</param>
    public sealed record EncryptedResponse(string? MessageId, CommandResponseMessage? Response);

    /// <summary>
    /// Checks whether a payload carries the encrypted wrapper, without decrypting it
    /// </summary>
    /// <param name="payload">The envelope payload</param>
    /// <returns><c>true</c> if the payload is an encrypted wrapper of the supported version; <c>false</c> otherwise</returns>
    public static bool IsEncrypted(string? payload)
        => TryParseWrapper(payload, out _);

    /// <summary>
    /// Decrypts a request payload with this client's private key and extracts the reply key
    /// </summary>
    /// <param name="payload">The envelope payload</param>
    /// <param name="privateKey">This client's private key</param>
    /// <param name="expectedMessageId">The envelope's message id; the request must be bound to the same id</param>
    /// <param name="replyKey">The portal's public key, to encrypt the response with</param>
    /// <returns>The command request</returns>
    /// <exception cref="CommandPayloadException">The payload is not encrypted, cannot be decrypted, is malformed, or is bound to another message</exception>
    public static CommandRequestMessage DecryptRequest(string? payload, RSA privateKey, string expectedMessageId, out Jwk replyKey)
    {
        if (!TryParseWrapper(payload, out var wrapper))
            throw new CommandPayloadException("The command payload is not end-to-end encrypted");

        var plaintext = Decrypt(wrapper, new Jwk(privateKey, true));

        EncryptedRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<EncryptedRequest>(plaintext, KeepRemoteConnection.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CommandPayloadException("The decrypted command payload is malformed", ex);
        }

        if (request?.Request == null)
            throw new CommandPayloadException("The decrypted command payload has no request");

        if (!string.Equals(request.MessageId, expectedMessageId, StringComparison.Ordinal))
            throw new CommandPayloadException("The command payload is bound to another message");

        replyKey = ParseReplyKey(request.ReplyKey);
        return request.Request;
    }

    /// <summary>
    /// Encrypts a response to the portal's reply key
    /// </summary>
    /// <param name="messageId">The envelope's message id, bound into the ciphertext</param>
    /// <param name="response">The command response</param>
    /// <param name="replyKey">The portal's public key, as received in the request</param>
    /// <returns>The wrapper to place in the envelope payload</returns>
    public static EncryptedPayload EncryptResponse(string messageId, CommandResponseMessage response, Jwk replyKey)
        => Encrypt(JsonSerializer.Serialize(new EncryptedResponse(messageId, response), KeepRemoteConnection.JsonOptions), replyKey);

    /// <summary>
    /// Encrypts a request to a client's public key. This is what the portal does; it is here so the
    /// format has a single reference implementation and can be tested end-to-end.
    /// </summary>
    /// <param name="messageId">The envelope's message id, bound into the ciphertext</param>
    /// <param name="request">The command request</param>
    /// <param name="replyKey">The portal's public key, for the client to encrypt the response to</param>
    /// <param name="clientPublicKey">The client's public key</param>
    /// <returns>The wrapper to place in the envelope payload</returns>
    public static EncryptedPayload EncryptRequest(string messageId, CommandRequestMessage request, Jwk replyKey, RSA clientPublicKey)
    {
        var replyKeyJson = JsonSerializer.Deserialize<JsonElement>(replyKey.ToJson(JWT.DefaultSettings.JsonMapper));
        var plaintext = JsonSerializer.Serialize(new EncryptedRequest(messageId, replyKeyJson, request), KeepRemoteConnection.JsonOptions);
        return Encrypt(plaintext, new Jwk(clientPublicKey, false));
    }

    /// <summary>
    /// Decrypts a response with the portal's reply private key. This is what the portal does; it is here so
    /// the format can be tested end-to-end.
    /// </summary>
    /// <param name="payload">The envelope payload</param>
    /// <param name="replyPrivateKey">The portal's private key</param>
    /// <param name="expectedMessageId">The envelope's message id; the response must be bound to the same id</param>
    /// <returns>The command response</returns>
    /// <exception cref="CommandPayloadException">The payload is not encrypted, cannot be decrypted, is malformed, or is bound to another message</exception>
    public static CommandResponseMessage DecryptResponse(string? payload, RSA replyPrivateKey, string expectedMessageId)
    {
        if (!TryParseWrapper(payload, out var wrapper))
            throw new CommandPayloadException("The command payload is not end-to-end encrypted");

        var plaintext = Decrypt(wrapper, new Jwk(replyPrivateKey, true));

        EncryptedResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<EncryptedResponse>(plaintext, KeepRemoteConnection.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CommandPayloadException("The decrypted command payload is malformed", ex);
        }

        if (response?.Response == null)
            throw new CommandPayloadException("The decrypted command payload has no response");
        if (!string.Equals(response.MessageId, expectedMessageId, StringComparison.Ordinal))
            throw new CommandPayloadException("The response belongs to another message");
        return response.Response;
    }

    /// <summary>
    /// Serializes the wrapper for the envelope payload
    /// </summary>
    /// <param name="payload">The wrapper</param>
    /// <returns>The JSON string</returns>
    public static string ToPayloadString(EncryptedPayload payload)
        => JsonSerializer.Serialize(payload, KeepRemoteConnection.JsonOptions);

    /// <summary>
    /// Encrypts a plaintext to a public key, compressing it first when it is large enough to be worth it
    /// </summary>
    /// <param name="plaintext">The plaintext</param>
    /// <param name="publicKey">The public key</param>
    /// <returns>The wrapper</returns>
    private static EncryptedPayload Encrypt(string plaintext, Jwk publicKey)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        if (bytes.Length <= CompressionThresholdBytes)
            return new EncryptedPayload(Version, JWT.EncodeBytes(bytes, publicKey, KeyAlgorithm, ContentEncryption), null);

        return new EncryptedPayload(Version, JWT.EncodeBytes(Compress(bytes), publicKey, KeyAlgorithm, ContentEncryption), GzipCompression);
    }

    /// <summary>
    /// Decrypts a wrapper and undoes the compression it names
    /// </summary>
    /// <param name="wrapper">The wrapper</param>
    /// <param name="privateKey">The private key</param>
    /// <returns>The plaintext</returns>
    /// <exception cref="CommandPayloadException">The payload cannot be decrypted or decompressed</exception>
    private static string Decrypt(EncryptedPayload wrapper, Jwk privateKey)
    {
        if (wrapper.Zip != null && wrapper.Zip != GzipCompression)
            throw new CommandPayloadException("The command payload uses an unsupported compression");

        byte[] bytes;
        try
        {
            bytes = JWT.DecodeBytes(wrapper.Jwe, privateKey, KeyAlgorithm, ContentEncryption);
        }
        catch (Exception ex)
        {
            throw new CommandPayloadException("The command payload could not be decrypted", ex);
        }

        if (wrapper.Zip == GzipCompression)
            bytes = Decompress(bytes);

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            throw new CommandPayloadException("The decrypted command payload is malformed", ex);
        }
    }

    /// <summary>
    /// Gzip compresses a plaintext
    /// </summary>
    /// <param name="bytes">The plaintext</param>
    /// <returns>The compressed bytes</returns>
    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    /// <summary>
    /// Gzip decompresses a plaintext, refusing to inflate beyond <see cref="MaxDecompressedBytes"/>
    /// </summary>
    /// <param name="bytes">The compressed bytes</param>
    /// <returns>The plaintext</returns>
    /// <exception cref="CommandPayloadException">The data is not valid gzip or inflates beyond the limit</exception>
    private static byte[] Decompress(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxDecompressedBytes)
                    throw new CommandPayloadException($"The decompressed command payload exceeds {MaxDecompressedBytes} bytes");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (CommandPayloadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CommandPayloadException("The command payload could not be decompressed", ex);
        }
    }

    /// <summary>
    /// Parses the wrapper, accepting only the supported version
    /// </summary>
    /// <param name="payload">The envelope payload</param>
    /// <param name="wrapper">The parsed wrapper</param>
    /// <returns><c>true</c> if the payload is a wrapper of the supported version; <c>false</c> otherwise</returns>
    private static bool TryParseWrapper(string? payload, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EncryptedPayload? wrapper)
    {
        wrapper = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != Version)
                return false;
            if (!root.TryGetProperty("jwe", out var jwe) || jwe.ValueKind != JsonValueKind.String)
                return false;

            var token = jwe.GetString();
            if (string.IsNullOrWhiteSpace(token))
                return false;

            string? zip = null;
            if (root.TryGetProperty("zip", out var zipElement) && zipElement.ValueKind != JsonValueKind.Null)
            {
                // The value is validated on decryption, so the reason can be reported; here it only has to be a string
                if (zipElement.ValueKind != JsonValueKind.String)
                    return false;
                zip = zipElement.GetString();
            }

            wrapper = new EncryptedPayload(v, token, zip);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses the reply key and verifies it is an RSA public key, so the response cannot be directed to a
    /// weaker key type and a private key is never accepted where a public key is expected
    /// </summary>
    /// <param name="replyKey">The reply key as JSON</param>
    /// <returns>The reply key</returns>
    private static Jwk ParseReplyKey(JsonElement replyKey)
    {
        if (replyKey.ValueKind != JsonValueKind.Object)
            throw new CommandPayloadException("The reply key is missing");

        if (!replyKey.TryGetProperty("kty", out var kty) || kty.GetString() != RsaKeyType)
            throw new CommandPayloadException("The reply key is not an RSA key");

        if (replyKey.TryGetProperty("d", out _))
            throw new CommandPayloadException("The reply key must be a public key");

        if (!replyKey.TryGetProperty("n", out _) || !replyKey.TryGetProperty("e", out _))
            throw new CommandPayloadException("The reply key is incomplete");

        try
        {
            var jwk = Jwk.FromJson(replyKey.GetRawText(), JWT.DefaultSettings.JsonMapper);
            // Materialize the key now, so a malformed modulus or exponent is caught before the command runs.
            // The Jwk caches and owns the RSA instance, so it must not be disposed here.
            _ = jwk.RsaKey();
            return jwk;
        }
        catch (CommandPayloadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CommandPayloadException("The reply key is invalid", ex);
        }
    }
}

/// <summary>
/// Thrown when a command payload does not meet the end-to-end encryption requirements
/// </summary>
public class CommandPayloadException : Exception
{
    /// <summary>
    /// Creates a new exception
    /// </summary>
    /// <param name="message">The reason</param>
    public CommandPayloadException(string message) : base(message) { }

    /// <summary>
    /// Creates a new exception
    /// </summary>
    /// <param name="message">The reason</param>
    /// <param name="innerException">The underlying failure</param>
    public CommandPayloadException(string message, Exception innerException) : base(message, innerException) { }
}
