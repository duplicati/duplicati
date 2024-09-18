// Copyright (C) 2024, The Duplicati Team
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
using System.Text;
using System.Text.Json;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// The type of messages that can be sent
/// </summary>
internal enum MessageType
{
    /// <summary>
    /// A pong response message
    /// </summary>
    Pong,
    /// <summary>
    /// An authentication message
    /// </summary>
    Welcome,
    /// <summary>
    /// An authentication message
    /// </summary>
    Auth,
    /// <summary>
    /// A command to forward
    /// </summary>
    Command,
    /// <summary>
    /// An unknown command
    /// </summary>
    Unknown
}

/// <summary>
/// A message to authenticate with
/// </summary>
/// <param name="Token">The server token</param>
/// <param name="Challenge">The server challenge</param>
internal sealed record AuthMessage(string? Token, string? Challenge);
/// <summary>
/// A message authentication response
/// </summary>
/// <param name="Accepted">Whether the authentication was accepted</param>
/// <param name="WillReplaceToken">Whether the token will be replaced</param>
/// <param name="NewToken">The new token</param>
/// <param name="SignedChallenge">The signed challenge</param>
internal sealed record AuthResultMessage(bool? Accepted, bool? WillReplaceToken, string? NewToken, string? SignedChallenge);

/// <summary>
/// A message to send a command
/// </summary>
/// <param name="Method">The HTTP method to use</param>
/// <param name="Path">The path to use</param>
/// <param name="Body">The optional body to send</param>
/// <param name="Headers">The optional headers to add</param>
public sealed record CommandRequestMessage(string Method, string Path, byte[]? Body, Dictionary<string, string>? Headers);

/// <summary>
/// A message to respond to a command
/// </summary>
/// <param name="StatusCode">The status code to return</param>
/// <param name="Body">The body to return</param>
/// <param name="Headers">The headers to return</param>
public sealed record CommandResponseMessage(int StatusCode, byte[]? Body, Dictionary<string, string>? Headers);

/// <summary>
/// The message envelope used for all communication
/// </summary>
internal sealed record EnvelopedMessage
{
    /// <summary>
    /// The sender of the message
    /// </summary>
    public required string From { get; init; }
    /// <summary>
    /// The recipient of the message
    /// </summary>
    public required string To { get; init; }
    /// <summary>
    /// The type of message
    /// </summary>
    public required string Type { get; init; }
    /// <summary>
    /// The unique message id
    /// </summary>
    public required string MessageId { get; init; }
    /// <summary>
    /// The payload of the message
    /// </summary>
    public string? Payload { get; init; }
    /// <summary>
    /// The signature of the payload
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Parses a raw message into an envelope, throwing on error
    /// </summary>
    /// <param name="rawMessage">The raw message to parse</param>
    /// <returns>The parsed envelope</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public static EnvelopedMessage ForceParse(string? rawMessage)
        => FromString(rawMessage ?? throw new EnvelopeJsonParsingException("Invalid Json message")) ?? throw new EnvelopeJsonParsingException("Invalid Json message");

    /// <summary>
    /// Parses a raw message into an envelope, returning null on error
    /// </summary>
    /// <param name="rawMessageBytes">The raw message to parse</param>
    /// <returns>The parsed envelope or null</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public static EnvelopedMessage? FromBytes(byte[] rawMessageBytes)
        => FromString(Encoding.UTF8.GetString(rawMessageBytes));

    /// <summary>
    /// Parses a raw message into an envelope, returning null on error
    /// </summary>
    /// <param name="rawMessage">The raw message to parse</param>
    /// <returns>The parsed envelope or null</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public static EnvelopedMessage? FromString(string rawMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<EnvelopedMessage>(rawMessage);
        }
        catch (JsonException jex)
        {
            throw new EnvelopeJsonParsingException("Invalid Json message", jex);
        }
    }

    /// <summary>
    /// Converts the envelope to a Json string
    /// </summary>
    /// <returns>The Json string representation of the envelope</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// Gets the payload of the message as a specific type
    /// </summary>
    /// <typeparam name="T">The type to parse the payload as</typeparam>
    /// <returns>The parsed payload</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public T GetPayload<T>()
        => JsonSerializer.Deserialize<T>(Payload ?? throw new EnvelopeJsonParsingException("Invalid Json message")) ?? throw new EnvelopeJsonParsingException("Invalid Json message");

    /// <summary>
    /// Computes the signature of the payload
    /// </summary>
    /// <param name="pemPrivatekey">The private key to use</param>
    /// <returns>The computed signature</returns>
    public string? ComputePayloadSignature(string? pemPrivatekey)
    {
        if (string.IsNullOrWhiteSpace(pemPrivatekey) || (Payload is null && MessageId is null))
            return null;

        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(pemPrivatekey);
        return BitConverter.ToString(
            rsa.SignData(Encoding.UTF8.GetBytes($"{Payload}::{MessageId}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)
        ).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Validates the message signature
    /// </summary>
    /// <param name="pemPublicKey">The public key for the server that sent</param>
    public void ValidateSignature(string? pemPublicKey)
    {
        if (string.IsNullOrWhiteSpace(Signature) || string.IsNullOrWhiteSpace(pemPublicKey) || (Payload is null && MessageId is null))
            throw new EnvelopeJsonParsingException("Invalid Json message");

        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(pemPublicKey);
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes($"{Payload}::{MessageId}"), Convert.FromHexString(Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new EnvelopeJsonParsingException("Invalid Json message");
    }

    /// <summary>
    /// Creates the signature on the returned message
    /// </summary>
    /// <param name="pemPrivatekey">The private key to use</param>
    /// <returns>The signed message</returns>
    public EnvelopedMessage WithSignature(string? pemPrivatekey)
        => this with { Signature = ComputePayloadSignature(pemPrivatekey) };

    /// <summary>
    /// Creates a new message with a payload
    /// </summary>
    /// <typeparam name="T">The type of payload</typeparam>
    /// <param name="payload">The payload to add</param>
    /// <param name="pemPrivatekey">The private key to use for signing</param>
    /// <returns>The new message</returns>
    public EnvelopedMessage WithPayload<T>(T payload, string? pemPrivatekey = null)
        => this with { Payload = JsonSerializer.Serialize(payload), Signature = ComputePayloadSignature(pemPrivatekey) };

    /// <summary>
    /// Creates a new envelope to respond to the current message
    /// </summary>
    /// <typeparam name="T">The type of payload</typeparam>
    /// <param name="payload">The payload to add</param>
    /// <param name="type">The type of message to send</param>
    /// <param name="pemPrivatekey">The private key to use for signing</param>
    /// <returns>The new message</returns>
    public EnvelopedMessage RespondWith<T>(T payload, string? type = null, string? pemPrivatekey = null)
        => new EnvelopedMessage
        {
            From = To,
            To = From,
            Type = type ?? Type,
            MessageId = MessageId,
            Payload = JsonSerializer.Serialize(payload),
            Signature = ComputePayloadSignature(pemPrivatekey)
        };

    /// <summary>
    /// Gets the type of message
    /// </summary>
    /// <returns>The type of message</returns>
    public MessageType GetMessageType()
    {
        return Type switch
        {
            "welcome" => MessageType.Welcome,
            "auth" => MessageType.Auth,
            "pong" => MessageType.Pong,
            "command" => MessageType.Command,
            _ => MessageType.Unknown
        };
    }
}