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
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.IPC;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the handshake that connects the server to a controller process
/// </summary>
public class ControllerPipeHandshakeTests
{
    [Test]
    [Category("IPC")]
    public void GeneratedValuesHaveTheExpectedShape()
    {
        var pipeName = ControllerPipeHandshake.CreatePipeName();
        var token = ControllerPipeHandshake.CreateToken();

        Assert.IsTrue(ControllerPipeHandshake.IsValidPipeName(pipeName), pipeName);
        Assert.IsTrue(ControllerPipeHandshake.IsValidToken(token), token);
        Assert.AreNotEqual(ControllerPipeHandshake.CreatePipeName(), pipeName);
        Assert.AreNotEqual(ControllerPipeHandshake.CreateToken(), token);

        Assert.IsFalse(ControllerPipeHandshake.IsValidPipeName(null));
        Assert.IsFalse(ControllerPipeHandshake.IsValidPipeName("dupl-../../etc/passwd"));
        Assert.IsFalse(ControllerPipeHandshake.IsValidPipeName(pipeName.ToUpperInvariant()));
        Assert.IsFalse(ControllerPipeHandshake.IsValidToken(null));
        Assert.IsFalse(ControllerPipeHandshake.IsValidToken(token + "0"));
        Assert.IsFalse(ControllerPipeHandshake.IsValidToken(token.Substring(1)));
    }

    [Test]
    [Category("IPC")]
    public void HandshakeRoundTripsThroughStdin()
    {
        var pipeName = ControllerPipeHandshake.CreatePipeName();
        var token = ControllerPipeHandshake.CreateToken();

        var stdin = new StringWriter();
        ControllerPipeHandshake.WriteHandshake(stdin, pipeName, token);

        var (readName, readToken) = ControllerPipeHandshake.ReadHandshake(new StringReader(stdin.ToString()));
        Assert.AreEqual(pipeName, readName);
        Assert.AreEqual(token, readToken);
    }

    [Test]
    [Category("IPC")]
    public void HandshakeRejectsMalformedStdin()
    {
        Assert.Throws<InvalidOperationException>(() => ControllerPipeHandshake.ReadHandshake(new StringReader("")));
        Assert.Throws<InvalidOperationException>(() => ControllerPipeHandshake.ReadHandshake(new StringReader("not-a-pipe\nnot-a-token\n")));
        Assert.Throws<InvalidOperationException>(() => ControllerPipeHandshake.ReadHandshake(new StringReader($"{ControllerPipeHandshake.CreatePipeName()}\n\n")));
    }

    [Test]
    [Category("IPC")]
    public async Task TokenIsVerifiedOverThePipe()
    {
        var pipeName = ControllerPipeHandshake.CreatePipeName();
        var token = ControllerPipeHandshake.CreateToken();

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accept = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await accept;

        // The token line is followed by the first RPC message, which must stay in the stream.
        // On Windows the pipe has no buffer, so a write only returns once the other end has read
        // it, and writing before reading in one sequence never returns. The client writes alongside
        // the reads, as the controller process does.
        var nextMessage = Encoding.ASCII.GetBytes("{\"jsonrpc\":\"2.0\"}\n");
        var write = Task.Run(async () =>
        {
            ControllerPipeHandshake.WriteToken(client, token);
            await client.WriteAsync(nextMessage, timeout.Token);
            await client.FlushAsync(timeout.Token);
        });

        var received = await ControllerPipeHandshake.ReadTokenAsync(server, timeout.Token);
        Assert.IsTrue(ControllerPipeHandshake.TokensMatch(token, received));
        Assert.IsFalse(ControllerPipeHandshake.TokensMatch(ControllerPipeHandshake.CreateToken(), received));
        Assert.IsFalse(ControllerPipeHandshake.TokensMatch(token, received + "0"));
        Assert.IsFalse(ControllerPipeHandshake.TokensMatch(token, ""));

        var rest = new byte[nextMessage.Length];
        var read = 0;
        while (read < rest.Length)
        {
            var n = await server.ReadAsync(rest.AsMemory(read), timeout.Token);
            Assert.Greater(n, 0);
            read += n;
        }
        Assert.AreEqual(nextMessage, rest);
        await write;
    }

    [Test]
    [Category("IPC")]
    public void TokenReadRejectsOverlongAndTruncatedLines()
    {
        var overlong = new MemoryStream(Encoding.ASCII.GetBytes(new string('a', 1000)));
        Assert.ThrowsAsync<InvalidDataException>(() => ControllerPipeHandshake.ReadTokenAsync(overlong, CancellationToken.None));

        var truncated = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        Assert.ThrowsAsync<EndOfStreamException>(() => ControllerPipeHandshake.ReadTokenAsync(truncated, CancellationToken.None));
    }
}
