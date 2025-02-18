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
/// <param name="Token">The client token</param>
/// <param name="PublicKey">The client public key</param>
/// <param name="Version">The version of the client</param>
public record AuthMessage(string Token, string PublicKey, string ClientVersion, int ProtocolVersion);

/// <summary>
/// A message authentication response
/// </summary>
/// <param name="Accepted">Whether the authentication was accepted</param>
/// <param name="WillReplaceToken">Whether the token will be replaced</param>
/// <param name="NewToken">The new token</param>
/// <param name="SignedChallenge">The signed challenge</param>
internal sealed record AuthResultMessage(bool? Accepted, bool? WillReplaceToken, string? NewToken);

/// <summary>
/// The welcome message from the server
/// </summary>
/// <param name="PublicKeyHash">The public key hash of the server key</param>
/// <param name="MachineName">The name of the machine</param>
/// <param name="ServerVersion">The version of the server</param>
public sealed record WelcomeMessage(string PublicKeyHash, string MachineName, string ServerVersion, IEnumerable<int> SupportedProtocolVersions);

/// <summary>
/// A message to send a command
/// </summary>
/// <param name="Method">The HTTP method to use</param>
/// <param name="Path">The path to use</param>
/// <param name="Body">The base64 encoded optional body to send</param>
/// <param name="Headers">The optional headers to add</param>
public sealed record CommandRequestMessage(string Method, string Path, string? Body, Dictionary<string, string>? Headers);

/// <summary>
/// A message to respond to a command
/// </summary>
/// <param name="StatusCode">The status code to return</param>
/// <param name="Body">The base64 encoded body to return</param>
/// <param name="Headers">The headers to return</param>
public sealed record CommandResponseMessage(int StatusCode, string? Body, Dictionary<string, string>? Headers);

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
    public required string? Payload { get; init; }
    /// <summary>
    /// The optional error message
    /// </summary>
    public required string? ErrorMessage { get; init; }

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
    /// <param name="rawMessage">The raw message to parse</param>
    /// <returns>The parsed envelope or null</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public static EnvelopedMessage? FromString(string rawMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<EnvelopedMessage>(rawMessage, options: KeepRemoteConnection.JsonOptions);
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
        return JsonSerializer.Serialize(this, options: KeepRemoteConnection.JsonOptions);
    }

    /// <summary>
    /// Gets the payload of the message as a specific type
    /// </summary>
    /// <typeparam name="T">The type to parse the payload as</typeparam>
    /// <returns>The parsed payload</returns>
    /// <exception cref="EnvelopeJsonParsingException">Thrown when the message is invalid</exception>
    public T GetPayload<T>()
        => JsonSerializer.Deserialize<T>(Payload ?? throw new EnvelopeJsonParsingException("Invalid Json message"), options: KeepRemoteConnection.JsonOptions)
            ?? throw new EnvelopeJsonParsingException("Invalid Json message");

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

    /// <summary>
    /// Responds to the message with a payload
    /// </summary>
    /// <typeparam name="T">The type of payload to respond with</typeparam>
    /// <param name="payload">The payload to respond with</param>
    /// <param name="type">The type of message to respond with</param>
    /// <returns>The response message</returns>
    public EnvelopedMessage RespondWith<T>(T payload, string? type = null)
        => new EnvelopedMessage
        {
            From = To,
            To = From,
            Type = type ?? Type,
            MessageId = MessageId,
            ErrorMessage = null,
            Payload = JsonSerializer.Serialize(payload, options: KeepRemoteConnection.JsonOptions)
        };
}