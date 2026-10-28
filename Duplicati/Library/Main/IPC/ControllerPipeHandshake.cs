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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// The handshake that connects the server to a controller process.
///
/// The RPC channel is a named pipe (a Unix domain socket on Linux and macOS) that only the
/// two processes hold, so the controller's stdout and stderr are ordinary log streams. The
/// server creates the pipe and starts the controller, hands it the pipe name and a one-time
/// token on its stdin, and accepts the connection only if the first line on the pipe is
/// that token. Stdin is used because neither the process list nor the environment shows it.
/// </summary>
public static class ControllerPipeHandshake
{
    /// <summary>
    /// The command line argument that puts the command line executable in controller server mode
    /// </summary>
    public const string COMMAND = "controller-server";

    /// <summary>
    /// The time the controller gets to connect to the pipe and present its token,
    /// and the time the server gets to accept the connection
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The prefix of generated pipe names
    /// </summary>
    private const string PIPE_NAME_PREFIX = "dupl-";

    /// <summary>
    /// The number of random bytes in a pipe name. The name is kept short because
    /// macOS limits the path of the socket that implements the pipe to 104 bytes,
    /// and the temporary folder it is placed in already takes about half of that.
    /// </summary>
    private const int PIPE_NAME_BYTES = 8;

    /// <summary>
    /// The number of random bytes in a token
    /// </summary>
    private const int TOKEN_BYTES = 32;

    /// <summary>
    /// The longest token line accepted from the pipe
    /// </summary>
    private const int MAX_TOKEN_LINE_LENGTH = 256;

    /// <summary>
    /// Creates a random pipe name
    /// </summary>
    public static string CreatePipeName()
        => PIPE_NAME_PREFIX + RandomHex(PIPE_NAME_BYTES);

    /// <summary>
    /// Creates a random one-time token
    /// </summary>
    public static string CreateToken()
        => RandomHex(TOKEN_BYTES);

    /// <summary>
    /// Returns <c>true</c> if the value has the shape of a generated pipe name
    /// </summary>
    public static bool IsValidPipeName(string? value)
        => value != null
            && value.StartsWith(PIPE_NAME_PREFIX, StringComparison.Ordinal)
            && IsLowerHex(value.Substring(PIPE_NAME_PREFIX.Length), PIPE_NAME_BYTES * 2);

    /// <summary>
    /// Returns <c>true</c> if the value has the shape of a generated token
    /// </summary>
    public static bool IsValidToken(string? value)
        => value != null && IsLowerHex(value, TOKEN_BYTES * 2);

    /// <summary>
    /// Writes the pipe name and the token to the controller's stdin
    /// </summary>
    /// <param name="stdin">The controller's stdin</param>
    /// <param name="pipeName">The pipe name</param>
    /// <param name="token">The token</param>
    public static void WriteHandshake(TextWriter stdin, string pipeName, string token)
    {
        stdin.Write(pipeName);
        stdin.Write('\n');
        stdin.Write(token);
        stdin.Write('\n');
        stdin.Flush();
    }

    /// <summary>
    /// Reads the pipe name and the token from the controller's stdin
    /// </summary>
    /// <param name="stdin">The controller's stdin</param>
    /// <returns>The pipe name and the token</returns>
    public static (string PipeName, string Token) ReadHandshake(TextReader stdin)
    {
        var pipeName = stdin.ReadLine();
        var token = stdin.ReadLine();
        if (!IsValidPipeName(pipeName) || !IsValidToken(token))
            throw new InvalidOperationException("The controller process did not receive a valid pipe name and token on stdin");

        return (pipeName!, token!);
    }

    /// <summary>
    /// Writes the token as the first line on the pipe
    /// </summary>
    /// <param name="pipe">The connected pipe</param>
    /// <param name="token">The token</param>
    public static void WriteToken(Stream pipe, string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token + "\n");
        pipe.Write(bytes, 0, bytes.Length);
        pipe.Flush();
    }

    /// <summary>
    /// Reads the token line from the pipe, one byte at a time so nothing after the
    /// newline is consumed from the stream that the RPC connection takes over next
    /// </summary>
    /// <param name="pipe">The connected pipe</param>
    /// <param name="cancellationToken">The token that stops the read</param>
    /// <returns>The received line, without the newline</returns>
    public static async Task<string> ReadTokenAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var line = new StringBuilder();
        while (true)
        {
            var read = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("The controller pipe was closed before a token was received");
            if (buffer[0] == (byte)'\n')
                return line.ToString();
            if (line.Length >= MAX_TOKEN_LINE_LENGTH)
                throw new InvalidDataException("The token line received on the controller pipe is too long");

            line.Append((char)buffer[0]);
        }
    }

    /// <summary>
    /// Compares the received token to the expected one in constant time
    /// </summary>
    /// <param name="expected">The token handed to the controller</param>
    /// <param name="received">The token received on the pipe</param>
    public static bool TokensMatch(string expected, string received)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(received));

    /// <summary>
    /// Returns a lowercase hex string of random bytes
    /// </summary>
    private static string RandomHex(int byteCount)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    /// <summary>
    /// Returns <c>true</c> if the value is lowercase hex of exactly the given length
    /// </summary>
    private static bool IsLowerHex(string value, int length)
        => value.Length == length && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}
