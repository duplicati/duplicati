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
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.CommandLine.ServerUtil;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The bridge between an MCP client on standard input and output and the MCP endpoint,
/// tested against a stand-in for the server
/// </summary>
[TestFixture]
[Category("Mcp")]
public class McpBridgeTests
{
    /// <summary>
    /// The time something that should happen at once is given to happen
    /// </summary>
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The time to wait before concluding that nothing is written
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private static readonly Uri Endpoint = new("http://localhost:8200/mcp");

    /// <summary>
    /// Stands in for the server: answers each request with what the test decides
    /// </summary>
    /// <param name="respond">Produces the response to a request</param>
    private sealed class StubHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        /// <summary>
        /// The requests received, with their bodies
        /// </summary>
        public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Enqueue((request, body));
            return await respond(request, body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A running bridge with the ends a client holds
    /// </summary>
    private sealed class BridgeHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToBridge = new();
        private readonly Pipe _bridgeToClient = new();
        private readonly StreamWriter _input;
        private readonly StreamReader _output;

        /// <summary>
        /// What the bridge has logged
        /// </summary>
        public StringWriter Log { get; } = new();

        /// <summary>
        /// The task that completes when the bridge stops
        /// </summary>
        public Task Running { get; }

        public BridgeHarness(Func<HttpClient> getClient, Func<CancellationToken, Task<bool>> reauthenticateAsync)
        {
            _input = new StreamWriter(_clientToBridge.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            _output = new StreamReader(_bridgeToClient.Reader.AsStream(), Encoding.UTF8);
            Running = new McpBridge(Endpoint, getClient, reauthenticateAsync, _clientToBridge.Reader.AsStream(), _bridgeToClient.Writer.AsStream(), Log)
                .RunAsync(CancellationToken.None);
        }

        /// <summary>
        /// Sends a message to the bridge, as a client does
        /// </summary>
        /// <param name="json">The message</param>
        /// <returns>An awaitable task</returns>
        public Task SendAsync(string json)
            => _input.WriteLineAsync(json);

        /// <summary>
        /// Reads the next message the bridge writes
        /// </summary>
        /// <returns>The message</returns>
        public async Task<JsonElement> ReadAsync()
        {
            var line = await _output.ReadLineAsync().WaitAsync(Prompt).ConfigureAwait(false);
            Assert.That(line, Is.Not.Null, "The bridge closed its output");
            return JsonDocument.Parse(line!).RootElement.Clone();
        }

        /// <summary>
        /// Closes the input, which is how a client stops the bridge
        /// </summary>
        /// <returns>An awaitable task</returns>
        public async Task CloseInputAsync()
        {
            await _input.DisposeAsync().ConfigureAwait(false);
            await _clientToBridge.Writer.CompleteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the bridge and requires that it wrote nothing that was not read
        /// </summary>
        /// <returns>An awaitable task</returns>
        public async ValueTask DisposeAsync()
        {
            await CloseInputAsync().ConfigureAwait(false);
            await Running.WaitAsync(Prompt).ConfigureAwait(false);
            await _bridgeToClient.Writer.CompleteAsync().ConfigureAwait(false);
            Assert.That(await _output.ReadToEndAsync().WaitAsync(Prompt).ConfigureAwait(false), Is.Empty, "The bridge wrote a message the test did not expect");
        }
    }

    /// <summary>
    /// Creates a response with a JSON body
    /// </summary>
    /// <param name="json">The body</param>
    /// <returns>The response</returns>
    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Creates the result of a request as the server would answer it
    /// </summary>
    /// <param name="body">The request</param>
    /// <returns>The response</returns>
    private static HttpResponseMessage Echo(string body)
        => Json($"{{\"jsonrpc\":\"2.0\",\"id\":{JsonDocument.Parse(body).RootElement.GetProperty("id").GetRawText()},\"result\":{{}}}}");

    /// <summary>
    /// Starts a bridge that uses one client and has no way to get new credentials
    /// </summary>
    /// <param name="handler">The stand-in for the server</param>
    /// <returns>The running bridge</returns>
    private static BridgeHarness Start(StubHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new BridgeHarness(() => client, _ => Task.FromResult(false));
    }

    [Test]
    public async Task ResponseIsWrittenAsOneLine_Async()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(Json("{\n  \"jsonrpc\": \"2.0\",\n  \"id\": \"a\",\n  \"result\": { \"text\": \"line one\\nline two\" }\n}")));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"method\":\"ping\"}").ConfigureAwait(false);
        var response = await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(response.GetProperty("id").GetString(), Is.EqualTo("a"));
        Assert.That(response.GetProperty("result").GetProperty("text").GetString(), Is.EqualTo("line one\nline two"));

        Assert.That(handler.Requests.TryDequeue(out var sent), Is.True);
        Assert.That(sent.Request.RequestUri, Is.EqualTo(Endpoint));
        Assert.That(sent.Request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(sent.Body, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"method\":\"ping\"}"));
        Assert.That(sent.Request.Headers.Accept.ToString(), Does.Contain("application/json").And.Contain("text/event-stream"));
    }

    [Test]
    public async Task EveryMessageOfAnEventStreamIsWritten_Async()
    {
        var stream = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":0.5}}\n\n"
            + ": a comment that is not a message\n\n"
            + "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\n";
        var handler = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") }));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"}").ConfigureAwait(false);

        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("method").GetString(), Is.EqualTo("notifications/progress"));
        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task NotificationGetsNoAnswer_Async()
    {
        var handler = new StubHandler((_, body, _) => Task.FromResult(body.Contains("\"id\"") ? Echo(body) : new HttpResponseMessage(HttpStatusCode.Accepted)));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}").ConfigureAwait(false);
        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}").ConfigureAwait(false);

        // The only message written is the answer to the request
        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task ProtocolVersionOfTheServerIsSentAfterInitialize_Async()
    {
        var handler = new StubHandler((_, body, _) => Task.FromResult(body.Contains("\"initialize\"")
            ? Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}")
            : Echo(body)));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}").ConfigureAwait(false);
        await bridge.ReadAsync().ConfigureAwait(false);
        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}").ConfigureAwait(false);
        await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(handler.Requests.TryDequeue(out var initialize), Is.True);
        Assert.That(initialize.Request.Headers.Contains("MCP-Protocol-Version"), Is.False);
        Assert.That(handler.Requests.TryDequeue(out var ping), Is.True);
        Assert.That(ping.Request.Headers.GetValues("MCP-Protocol-Version"), Is.EqualTo(new[] { "2025-06-18" }));
    }

    [TestCase(HttpStatusCode.NotFound, "not enabled")]
    [TestCase(HttpStatusCode.Unauthorized, "refused the credentials")]
    [TestCase(HttpStatusCode.Forbidden, "refused the request")]
    [TestCase(HttpStatusCode.InternalServerError, "status 500")]
    public async Task RefusedRequestIsAnsweredWithAnError_Async(HttpStatusCode status, string expected)
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(status)));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"ping\"}").ConfigureAwait(false);
        var response = await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(response.GetProperty("id").GetInt32(), Is.EqualTo(5));
        Assert.That(response.GetProperty("error").GetProperty("message").GetString(), Does.Contain(expected));
        Assert.That(bridge.Log.ToString(), Does.Contain(expected), "The failure is also logged, away from the output");
    }

    [Test]
    public async Task UnreachableServerIsAnsweredWithAnError_Async()
    {
        var handler = new StubHandler((_, _, _) => throw new HttpRequestException("Connection refused"));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"ping\"}").ConfigureAwait(false);
        var response = await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(response.GetProperty("id").GetInt32(), Is.EqualTo(6));
        Assert.That(response.GetProperty("error").GetProperty("message").GetString(), Does.Contain("could not be reached").And.Contain("Connection refused"));
    }

    /// <summary>
    /// A login gives a short-lived access token, so the bridge logs in again when the server
    /// refuses it, and sends the request once more
    /// </summary>
    [Test]
    public async Task RefusedCredentialsAreReplacedAndTheRequestIsSentAgain_Async()
    {
        var expired = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var renewed = new StubHandler((_, body, _) => Task.FromResult(Echo(body)));
        var client = new HttpClient(expired);
        var logins = 0;
        await using var bridge = new BridgeHarness(() => client, _ =>
        {
            Interlocked.Increment(ref logins);
            client = new HttpClient(renewed);
            return Task.FromResult(true);
        });

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"ping\"}").ConfigureAwait(false);
        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(7));

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"ping\"}").ConfigureAwait(false);
        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(8));

        Assert.That(logins, Is.EqualTo(1));
        Assert.That(expired.Requests, Has.Count.EqualTo(1));
        Assert.That(renewed.Requests, Has.Count.EqualTo(2), "The refused request is sent again, and the next one uses the new credentials from the start");
    }

    [Test]
    public async Task RefusedCredentialsThatCannotBeReplacedAreAnError_Async()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var client = new HttpClient(handler);
        var logins = 0;
        await using var bridge = new BridgeHarness(() => client, _ =>
        {
            Interlocked.Increment(ref logins);
            return Task.FromResult(false);
        });

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}").ConfigureAwait(false);
        var response = await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(response.GetProperty("error").GetProperty("message").GetString(), Does.Contain("refused the credentials"));
        Assert.That(logins, Is.EqualTo(1));
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "The request is not sent again with the credentials that were refused");
    }

    /// <summary>
    /// The server keeps no state between requests, so the bridge cancels a request by closing
    /// its connection, and the client gets no answer to a request it has cancelled
    /// </summary>
    [Test]
    public async Task CancelledRequestIsClosedAndGetsNoAnswer_Async()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, body, cancellationToken) =>
        {
            if (!body.Contains("\"slow\""))
                return Echo(body);

            started.SetResult();
            using var registration = cancellationToken.Register(() => closed.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Not reached");
        });
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"slow\"}").ConfigureAwait(false);
        await started.Task.WaitAsync(Prompt).ConfigureAwait(false);

        // A cancellation of another request leaves this one alone
        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":11}}").ConfigureAwait(false);
        await Task.Delay(Quiet).ConfigureAwait(false);
        Assert.That(closed.Task.IsCompleted, Is.False);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":10}}").ConfigureAwait(false);
        await closed.Task.WaitAsync(Prompt).ConfigureAwait(false);

        // The bridge keeps working, and the next message written is the answer to the next request
        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"ping\"}").ConfigureAwait(false);
        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(12));
        Assert.That(handler.Requests, Has.Count.EqualTo(2), "The cancellation is not forwarded to the server");
    }

    /// <summary>
    /// A request that is cancelled by something else than the client, such as a timeout of the
    /// connection, must still be answered, or the client waits for it forever
    /// </summary>
    [Test]
    public async Task RequestCancelledByTheConnectionIsAnsweredWithAnError_Async()
    {
        var handler = new StubHandler((_, _, _) => throw new TaskCanceledException("The request timed out"));
        await using var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":13,\"method\":\"ping\"}").ConfigureAwait(false);
        var response = await bridge.ReadAsync().ConfigureAwait(false);

        Assert.That(response.GetProperty("id").GetInt32(), Is.EqualTo(13));
        Assert.That(response.GetProperty("error").GetProperty("message").GetString(), Does.Contain("timed out"));
    }

    [Test]
    public async Task EndOfInputClosesTheRequestsThatAreWaiting_Async()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Not reached");
        });
        var bridge = Start(handler);

        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":14,\"method\":\"tools/call\"}").ConfigureAwait(false);
        await started.Task.WaitAsync(Prompt).ConfigureAwait(false);
        Assert.That(bridge.Running.IsCompleted, Is.False);

        // Stopping requires that the bridge ends without writing an answer nobody is there to read
        await bridge.DisposeAsync().ConfigureAwait(false);
        Assert.That(bridge.Running.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task MessageThatIsNotJsonIsReportedWithoutStoppingTheBridge_Async()
    {
        var handler = new StubHandler((_, body, _) => Task.FromResult(Echo(body)));
        var bridge = Start(handler);

        await bridge.SendAsync("this is not JSON").ConfigureAwait(false);
        await bridge.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":15,\"method\":\"ping\"}").ConfigureAwait(false);

        Assert.That((await bridge.ReadAsync().ConfigureAwait(false)).GetProperty("id").GetInt32(), Is.EqualTo(15));

        // Messages are handled side by side, so the bridge is stopped before looking at what it did with the first one
        await bridge.DisposeAsync().ConfigureAwait(false);
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "A message that cannot be read is not sent to the server");
        Assert.That(bridge.Log.ToString(), Is.Not.Empty);
    }
}
