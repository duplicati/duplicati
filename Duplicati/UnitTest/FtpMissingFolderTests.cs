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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using FluentFTP.Exceptions;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers how the ftp backend reports a missing folder when the server refuses the listing
    /// with a code other than 550 or 450, as pyftpdlib does with 501 for MLSD.
    /// </summary>
    [TestFixture]
    public class FtpMissingFolderTests
    {
        [Test]
        public void MissingFolderRefusedWith501IsReportedAsMissing()
        {
            using var server = new ScriptedFtpServer(existingFolders: ["/"], listRefusal: "501 No such directory.");
            using var backend = new FTP($"ftp://127.0.0.1:{server.Port}/missing", Options());

            var ex = Assert.ThrowsAsync<FolderMissingException>(() => backend.TestAsync(false, CancellationToken.None));
            Assert.That(ex!.Message, Does.Contain("No such directory"));
        }

        [Test]
        public void ExistingFolderRefusedWith501KeepsTheServerError()
        {
            using var server = new ScriptedFtpServer(existingFolders: ["/", "/backup"], listRefusal: "501 Listing refused.");
            using var backend = new FTP($"ftp://127.0.0.1:{server.Port}/backup", Options());

            var ex = Assert.ThrowsAsync<FtpCommandException>(() => backend.TestAsync(false, CancellationToken.None));
            Assert.That(ex!.CompletionCode, Is.EqualTo("501"));
        }

        [Test]
        public async Task ExistingFolderIsListed()
        {
            using var server = new ScriptedFtpServer(existingFolders: ["/", "/backup"], listRefusal: null);
            using var backend = new FTP($"ftp://127.0.0.1:{server.Port}/backup", Options());

            await backend.TestAsync(false, CancellationToken.None);
        }

        private static Dictionary<string, string?> Options() => new()
        {
            ["auth-username"] = "tester",
            ["auth-password"] = "fake-ftp-pass-1",
            ["ftp-data-connection-type"] = "PASV",
        };

        /// <summary>
        /// A minimal ftp server that answers the commands the backend sends. Listings of a missing folder,
        /// or every listing when <c>listRefusal</c> is set, are refused with that reply.
        /// </summary>
        private sealed class ScriptedFtpServer : IDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly HashSet<string> _folders;
            private readonly string? _listRefusal;
            private readonly CancellationTokenSource _cts = new();

            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

            public ScriptedFtpServer(string[] existingFolders, string? listRefusal)
            {
                _folders = new HashSet<string>(existingFolders);
                _listRefusal = listRefusal;
                _listener.Start();
                _ = AcceptAsync();
            }

            private async Task AcceptAsync()
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                        _ = ServeAsync(client);
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested) { }
                catch (ObjectDisposedException) { }
            }

            private async Task ServeAsync(TcpClient client)
            {
                using var _ = client;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                TcpListener? passive = null;
                var cwd = "/";

                try
                {
                    await writer.WriteLineAsync("220 Scripted test server").ConfigureAwait(false);
                    while (await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false) is { } line)
                    {
                        var space = line.IndexOf(' ');
                        var command = (space < 0 ? line : line[..space]).ToUpperInvariant();
                        var argument = space < 0 ? string.Empty : line[(space + 1)..];

                        switch (command)
                        {
                            case "USER":
                                await writer.WriteLineAsync("331 Password required").ConfigureAwait(false);
                                break;
                            case "PASS":
                                await writer.WriteLineAsync("230 Logged in").ConfigureAwait(false);
                                break;
                            case "FEAT":
                                await writer.WriteLineAsync("211-Features:\r\n MLST type*;size*;modify*;\r\n211 End").ConfigureAwait(false);
                                break;
                            case "SYST":
                                await writer.WriteLineAsync("215 UNIX Type: L8").ConfigureAwait(false);
                                break;
                            case "PWD":
                                await writer.WriteLineAsync($"257 \"{cwd}\" is the current directory").ConfigureAwait(false);
                                break;
                            case "CWD":
                                var target = argument.StartsWith('/') ? argument : (cwd.TrimEnd('/') + "/" + argument);
                                target = target.Length > 1 ? target.TrimEnd('/') : target;
                                if (_folders.Contains(target))
                                {
                                    cwd = target;
                                    await writer.WriteLineAsync("250 Directory changed").ConfigureAwait(false);
                                }
                                else
                                    await writer.WriteLineAsync("550 No such file or directory").ConfigureAwait(false);
                                break;
                            case "TYPE":
                            case "OPTS":
                            case "NOOP":
                                await writer.WriteLineAsync("200 OK").ConfigureAwait(false);
                                break;
                            case "PASV":
                                passive?.Stop();
                                passive = new TcpListener(IPAddress.Loopback, 0);
                                passive.Start();
                                var port = ((IPEndPoint)passive.LocalEndpoint).Port;
                                await writer.WriteLineAsync($"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})").ConfigureAwait(false);
                                break;
                            case "MLSD":
                            case "LIST":
                            case "NLST":
                                var path = argument.Length > 1 ? argument.TrimEnd('/') : argument;
                                if (_listRefusal != null || !_folders.Contains(path))
                                {
                                    await writer.WriteLineAsync(_listRefusal ?? "501 No such directory.").ConfigureAwait(false);
                                    passive?.Stop();
                                    passive = null;
                                    break;
                                }

                                await writer.WriteLineAsync("150 Opening data connection").ConfigureAwait(false);
                                if (passive != null)
                                {
                                    using (var data = await passive.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false)) { }
                                    passive.Stop();
                                    passive = null;
                                }
                                await writer.WriteLineAsync("226 Transfer complete").ConfigureAwait(false);
                                break;
                            case "QUIT":
                                await writer.WriteLineAsync("221 Bye").ConfigureAwait(false);
                                return;
                            default:
                                await writer.WriteLineAsync("502 Command not implemented").ConfigureAwait(false);
                                break;
                        }
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested) { }
                catch (IOException) { }
                finally
                {
                    passive?.Stop();
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _cts.Dispose();
            }
        }
    }
}
