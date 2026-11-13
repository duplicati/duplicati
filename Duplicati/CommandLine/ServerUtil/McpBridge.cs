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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json.Nodes;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Bridges an MCP client that talks over standard input and output to the MCP endpoint of the server,
/// which talks over HTTP. Each message read from the input is posted to the endpoint, and every message
/// in the response is written to the output. The bridge does not interpret the tools; it only moves messages.
/// </summary>
/// <param name="endpoint">The address of the MCP endpoint</param>
/// <param name="getClient">Returns the HTTP client to use, which carries the credentials</param>
/// <param name="reauthenticateAsync">Called when the server refuses the credentials; returns <c>true</c> if new credentials were obtained and the request should be sent again</param>
/// <param name="input">The stream the MCP client writes to</param>
/// <param name="output">The stream the MCP client reads from</param>
/// <param name="log">Where diagnostics are written; never the output stream</param>
public sealed class McpBridge(
    Uri endpoint,
    Func<HttpClient> getClient,
    Func<CancellationToken, Task<bool>> reauthenticateAsync,
    Stream input,
    Stream output,
    TextWriter log)
{
    /// <summary>
    /// The header that tells the server which protocol revision the client speaks
    /// </summary>
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";

    /// <summary>
    /// The JSON-RPC error code for a failure in the bridge or the server, from the range reserved for implementations
    /// </summary>
    private const int BridgeErrorCode = -32000;

    /// <summary>
    /// The encoding of the messages, without a byte order mark
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// Serializes writes to the output, as responses arrive concurrently
    /// </summary>
    private readonly SemaphoreSlim _outputLock = new(1, 1);

    /// <summary>
    /// Serializes attempts to get new credentials
    /// </summary>
    private readonly SemaphoreSlim _reauthenticateLock = new(1, 1);

    /// <summary>
    /// The requests that are waiting for a response, by the JSON text of their ID, so the client can cancel them
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

    /// <summary>
    /// The protocol revision the server chose when the client initialized, sent with the requests that follow
    /// </summary>
    private volatile string? _protocolVersion;

    /// <summary>
    /// Runs the bridge until the input ends or the token is cancelled
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task that completes when the bridge has stopped</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(input, Utf8);
        var pending = new ConcurrentDictionary<Task, byte>();

        // Cancels every request when the input ends, also one that has been read but has not started yet
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = stopping.Token;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Messages are handled concurrently, so a long wait does not hold up the ones that follow
            var task = Task.Run(() => HandleMessageAsync(line, stoppingToken), CancellationToken.None);
            pending[task] = 0;
            _ = task.ContinueWith(t => pending.TryRemove(t, out _), TaskScheduler.Default);
        }

        // The client has gone, so nobody is waiting for the answers
        stopping.Cancel();
        await Task.WhenAll(pending.Keys).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles one message from the client; it never throws
    /// </summary>
    /// <param name="message">The message, as one line of JSON</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        JsonNode? id = null;
        string? idKey = null;
        CancellationTokenSource? cts = null;
        try
        {
            string? method = null;
            if (JsonNode.Parse(message) is JsonObject parsed)
            {
                method = parsed["method"]?.GetValue<string>();
                id = parsed["id"]?.DeepClone();

                // The server keeps no state between requests, so it cannot match a cancellation to the
                // request it is for. The request is cancelled by closing its connection instead.
                if (method == "notifications/cancelled")
                {
                    var cancelledId = parsed["params"]?["requestId"]?.ToJsonString();
                    if (cancelledId != null && _inFlight.TryGetValue(cancelledId, out var cancelled))
                        cancelled.Cancel();
                    return;
                }
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (id != null && method != null)
            {
                idKey = id.ToJsonString();
                _inFlight[idKey] = cts;
            }

            using var response = await SendAsync(message, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await WriteErrorAsync(id, DescribeFailure(response.StatusCode)).ConfigureAwait(false);
                return;
            }

            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
                return;

            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                await foreach (var item in SseParser.Create(stream).EnumerateAsync(cts.Token).ConfigureAwait(false))
                    if (!string.IsNullOrWhiteSpace(item.Data))
                        await WriteMessageAsync(item.Data, method).ConfigureAwait(false);
            }
            else
            {
                await WriteMessageAsync(await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false), method).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cts == null || cts.IsCancellationRequested)
        {
            // Cancelled by the client, or the bridge is stopping; neither expects an answer.
            // A request that was cancelled for another reason, such as a timeout, gets an error as its answer.
        }
        catch (Exception ex)
        {
            log.WriteLine($"MCP bridge: {ex.Message}");
            await WriteErrorAsync(id, $"The Duplicati server could not be reached at {endpoint}: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            if (idKey != null)
                _inFlight.TryRemove(idKey, out _);
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Posts a message to the endpoint, once more with new credentials if the first attempt is refused
    /// </summary>
    /// <param name="message">The message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The response, with the content not yet read</returns>
    private async Task<HttpResponseMessage> SendAsync(string message, CancellationToken cancellationToken)
    {
        var client = getClient();
        var response = await PostAsync(client, message, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        await _reauthenticateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have obtained new credentials while this one waited
            if (ReferenceEquals(client, getClient()) && !await reauthenticateAsync(cancellationToken).ConfigureAwait(false))
                return response;
        }
        finally
        {
            _reauthenticateLock.Release();
        }

        response.Dispose();
        return await PostAsync(getClient(), message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a message to the endpoint
    /// </summary>
    /// <param name="client">The client to use</param>
    /// <param name="message">The message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The response, with the content not yet read</returns>
    private Task<HttpResponseMessage> PostAsync(HttpClient client, string message, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(message, Utf8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_protocolVersion is { } version)
            request.Headers.TryAddWithoutValidation(ProtocolVersionHeader, version);

        // The response can be a stream of progress messages, so it is read as it arrives
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Writes a message from the server to the client, as one line
    /// </summary>
    /// <param name="message">The message as JSON</param>
    /// <param name="requestMethod">The method of the request the message answers, if known</param>
    /// <returns>An awaitable task</returns>
    private async Task WriteMessageAsync(string message, string? requestMethod)
    {
        // Parsing and writing it again puts it on one line, which is what separates messages on the output
        var node = JsonNode.Parse(message);
        if (node == null)
            return;

        if (requestMethod == "initialize" && node["result"]?["protocolVersion"]?.GetValue<string>() is { } version)
            _protocolVersion = version;

        await WriteLineAsync(node.ToJsonString()).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an error response to the client, so a request that cannot be answered does not leave it waiting
    /// </summary>
    /// <param name="id">The ID of the request, or <c>null</c> if the message was a notification, which gets no response</param>
    /// <param name="message">The error message</param>
    /// <returns>An awaitable task</returns>
    private async Task WriteErrorAsync(JsonNode? id, string message)
    {
        log.WriteLine($"MCP bridge: {message}");
        if (id == null)
            return;

        var error = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject { ["code"] = BridgeErrorCode, ["message"] = message }
        };
        await WriteLineAsync(error.ToJsonString()).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one line to the output
    /// </summary>
    /// <param name="line">The line, without the line break</param>
    /// <returns>An awaitable task</returns>
    private async Task WriteLineAsync(string line)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        await _outputLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await output.WriteAsync(bytes).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _outputLock.Release();
        }
    }

    /// <summary>
    /// Explains a refused request in terms of what the user can do about it
    /// </summary>
    /// <param name="statusCode">The status the server answered with</param>
    /// <returns>The explanation</returns>
    private string DescribeFailure(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.NotFound => $"The MCP endpoint is not enabled on the Duplicati server at {endpoint}. Enable it in the settings, or start the server with --webservice-enable-mcp-server.",
            HttpStatusCode.Unauthorized => "The Duplicati server refused the credentials. The access token may have expired or been deleted.",
            HttpStatusCode.Forbidden => "The Duplicati server refused the request. Check that the host name in the address is one the server allows.",
            _ => $"The Duplicati server answered with status {(int)statusCode}."
        };
}
