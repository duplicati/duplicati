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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// What an MCP token allows; each level includes the ones before it
/// </summary>
public enum McpTokenLevel
{
    /// <summary>
    /// List backups, follow tasks and browse backup contents
    /// </summary>
    Read,
    /// <summary>
    /// Also run backups and stop tasks
    /// </summary>
    Backup,
    /// <summary>
    /// Also restore files
    /// </summary>
    Restore
}

/// <summary>
/// The description of an issued MCP token. The token itself is not stored.
/// </summary>
/// <param name="Id">The ID of the token</param>
/// <param name="Name">The name given to the token, usually the agent it is for</param>
/// <param name="Created">The time the token was created, in UTC</param>
/// <param name="Expires">The time the token expires, in UTC</param>
/// <param name="Level">What the token allows</param>
/// <param name="BackupIds">The IDs of the backups the token can be used with, or <c>null</c> for all backups</param>
/// <param name="RestoreRoots">The folders the token can restore into, besides the original location of the files</param>
public sealed record McpTokenEntry(
    string Id,
    string Name,
    DateTime Created,
    DateTime Expires,
    McpTokenLevel Level,
    string[]? BackupIds,
    string[] RestoreRoots);

/// <summary>
/// The store of issued MCP tokens
/// </summary>
public interface IMcpTokenStore
{
    /// <summary>
    /// Lists the issued tokens, including expired ones
    /// </summary>
    /// <returns>The tokens, oldest first</returns>
    IReadOnlyList<McpTokenEntry> List();

    /// <summary>
    /// Gets an issued token
    /// </summary>
    /// <param name="id">The ID of the token</param>
    /// <returns>The token, or <c>null</c> if it does not exist or has been deleted</returns>
    McpTokenEntry? Get(string id);

    /// <summary>
    /// Issues a token
    /// </summary>
    /// <param name="name">The name of the token</param>
    /// <param name="level">What the token allows</param>
    /// <param name="backupIds">The IDs of the backups the token can be used with, or <c>null</c> for all backups</param>
    /// <param name="restoreRoots">The folders the token can restore into, besides the original location of the files</param>
    /// <param name="lifetime">How long the token is valid</param>
    /// <returns>The description of the token, and the token to give to the agent</returns>
    /// <exception cref="Exceptions.NotFoundException">The MCP server is turned off, which is when no token can be issued</exception>
    (McpTokenEntry Entry, string Token) Create(string name, McpTokenLevel level, string[]? backupIds, string[] restoreRoots, TimeSpan lifetime);

    /// <summary>
    /// Deletes a token, which makes it invalid at once
    /// </summary>
    /// <param name="id">The ID of the token</param>
    /// <returns><c>true</c> if the token existed; turning the MCP server off deletes all tokens, so none exists then</returns>
    bool Delete(string id);
}
