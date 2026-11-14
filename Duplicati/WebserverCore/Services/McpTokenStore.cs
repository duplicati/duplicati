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

using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Keeps the issued MCP tokens in the server settings
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="tokenProvider">The provider creating the tokens</param>
public class McpTokenStore(Connection connection, IJWTTokenProvider tokenProvider) : IMcpTokenStore
{
    /// <summary>
    /// The options used to store the list
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc/>
    public IReadOnlyList<McpTokenEntry> List()
        => Parse(connection.ApplicationSettings.McpTokens);

    /// <inheritdoc/>
    public McpTokenEntry? Get(string id)
        => List().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    /// <inheritdoc/>
    public (McpTokenEntry Entry, string Token) Create(string name, McpTokenLevel level, string[]? backupIds, string[] restoreRoots, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var entry = new McpTokenEntry(
            Guid.NewGuid().ToString("N"),
            name,
            now,
            now.Add(lifetime),
            level,
            backupIds,
            restoreRoots);

        // The settings refuse the change when the MCP server is off, in the same step as they make it
        var added = connection.ApplicationSettings.UpdateMcpTokens(json =>
        {
            var entries = Parse(json);
            entries.Add(entry);
            return JsonSerializer.Serialize(entries, JsonOptions);
        });
        if (!added)
            throw new NotFoundException(DisabledMessage);

        return (entry, tokenProvider.CreateMcpToken(entry.Id, entry.Expires.ToLocalTime()));
    }

    /// <inheritdoc/>
    public bool Delete(string id)
    {
        var removed = false;
        connection.ApplicationSettings.UpdateMcpTokens(json =>
        {
            var entries = Parse(json);
            removed = entries.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal)) > 0;
            return removed ? JsonSerializer.Serialize(entries, JsonOptions) : json;
        });

        return removed;
    }

    /// <summary>
    /// The message for a request to issue a token while the MCP server is off
    /// </summary>
    public const string DisabledMessage = "The MCP server is not enabled. Enable it in the settings before issuing access tokens.";

    /// <summary>
    /// Reads a stored list
    /// </summary>
    /// <param name="json">The list as stored in the settings</param>
    /// <returns>The list</returns>
    private static List<McpTokenEntry> Parse(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<McpTokenEntry>>(json, JsonOptions) ?? [];
}
