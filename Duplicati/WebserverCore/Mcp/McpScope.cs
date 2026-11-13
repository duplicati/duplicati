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

using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using ModelContextProtocol;

namespace Duplicati.WebserverCore.Mcp;

/// <summary>
/// What the caller of an MCP tool is allowed to do.
/// A caller with an MCP token is limited by the token; a caller with the credentials of the API is not limited.
/// </summary>
/// <param name="httpContextAccessor">The accessor of the request the tool call arrived in</param>
/// <param name="tokenStore">The store of issued tokens</param>
public class McpScope(IHttpContextAccessor httpContextAccessor, IMcpTokenStore tokenStore)
{
    /// <summary>
    /// The MCP token the call was made with, or <c>null</c> if the caller has full access
    /// </summary>
    private readonly McpTokenEntry? _token = ResolveToken(httpContextAccessor, tokenStore);

    /// <summary>
    /// The name of the caller, for the log
    /// </summary>
    public string CallerName => _token == null ? "a full access credential" : $"MCP token \"{_token.Name}\"";

    /// <summary>
    /// Finds the token of the current request
    /// </summary>
    /// <param name="httpContextAccessor">The accessor of the request</param>
    /// <param name="tokenStore">The store of issued tokens</param>
    /// <returns>The token, or <c>null</c> if the caller has full access</returns>
    private static McpTokenEntry? ResolveToken(IHttpContextAccessor httpContextAccessor, IMcpTokenStore tokenStore)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new McpException("The caller of the tool could not be determined.");

        // A request that also carries the credentials of the API is not limited by a token
        if (user.Identities.Any(x => x.IsAuthenticated && x.AuthenticationType != McpTokenHandler.SchemeName))
            return null;

        var tokenId = user.FindFirst(McpTokenHandler.TokenIdClaim)?.Value;
        return (tokenId == null ? null : tokenStore.Get(tokenId))
            ?? throw new McpException("The MCP token is no longer valid.");
    }

    /// <summary>
    /// Requires that the caller has at least the given level
    /// </summary>
    /// <param name="level">The level the tool needs</param>
    public void RequireLevel(McpTokenLevel level)
    {
        if (_token != null && _token.Level < level)
            throw new McpException($"This MCP token has the permission level {_token.Level}, and the tool needs {level}. Ask the user for a token with that level.");
    }

    /// <summary>
    /// Checks if the caller can use a backup
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <returns><c>true</c> if the backup is within the scope of the caller</returns>
    public bool CanAccessBackup(string? backupId)
        => _token?.BackupIds == null || (backupId != null && _token.BackupIds.Contains(backupId, StringComparer.Ordinal));

    /// <summary>
    /// Requires that a restore target is one the caller can write to, and returns the path to restore to.
    /// A token can always restore files to where they were backed up from, and
    /// to other folders only below the restore roots it was issued with.
    /// </summary>
    /// <remarks>
    /// The path returned for a token has its links replaced by what they point to, so the restore
    /// writes to the folder that was checked. This confines the path the restore is given, and no more.
    /// A link that is created in that path after this check, and before the restore runs, is not seen.
    /// Neither is a link inside the folder: the restore follows it when a folder in the backup has the
    /// name of the link, as the check it makes of each path it writes
    /// (<see cref="Library.Common.IO.Util.IsPathInsideTarget"/>) looks at the last existing folder only.
    /// The restore roots are therefore not a boundary against someone who can create links below them.
    /// </remarks>
    /// <param name="restorePath">The folder to restore into, or <c>null</c> for the original location</param>
    /// <returns>The folder to restore into, or <c>null</c> for the original location</returns>
    public string? ResolveRestorePath(string? restorePath)
    {
        if (_token == null || string.IsNullOrWhiteSpace(restorePath))
            return restorePath;

        var expanded = SpecialFolders.ExpandEnvironmentVariables(restorePath);
        if (!Path.IsPathFullyQualified(expanded))
            throw new McpException("Give restorePath as a full path, or leave it out to restore to the original location.");

        string target;
        string[] roots;
        try
        {
            // Every folder of the path is examined, which the check the restore makes of its paths does not do
            target = Library.Common.IO.Util.ResolveAllLinks(expanded);
            roots = _token.RestoreRoots.Where(Path.IsPathFullyQualified).Select(Library.Common.IO.Util.ResolveAllLinks).ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new McpException($"The restore path cannot be used: {ex.Message}");
        }

        // The restore expands environment variables in the path it is given, which must not change the checked path
        if (!string.Equals(SpecialFolders.ExpandEnvironmentVariables(target), target, StringComparison.Ordinal))
            throw new McpException("The restore path holds the name of an environment variable after its links are followed, which is not supported.");

        var comparable = Library.Common.IO.Util.AppendDirSeparator(target);
        if (!roots.Any(root => comparable.StartsWith(Library.Common.IO.Util.AppendDirSeparator(root), Library.Utility.Utility.ClientFilenameStringComparison)))
            throw new McpException(_token.RestoreRoots.Length == 0
                ? "This MCP token can only restore files to their original location. Leave restorePath out."
                : $"This MCP token can only restore to the original location, or below: {string.Join(", ", _token.RestoreRoots)}");

        return target;
    }
}
