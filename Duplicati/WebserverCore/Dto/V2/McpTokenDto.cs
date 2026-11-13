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

using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// The description of an issued MCP token
/// </summary>
/// <param name="Id">The ID of the token</param>
/// <param name="Name">The name given to the token</param>
/// <param name="Created">The time the token was created, in UTC</param>
/// <param name="Expires">The time the token expires, in UTC</param>
/// <param name="Expired">True if the token has expired</param>
/// <param name="Level">What the token allows</param>
/// <param name="BackupIds">The IDs of the backups the token can be used with, or <c>null</c> for all backups</param>
/// <param name="RestoreRoots">The folders the token can restore into, besides the original location of the files</param>
public sealed record McpTokenDto(
    string Id,
    string Name,
    DateTime Created,
    DateTime Expires,
    bool Expired,
    McpTokenLevel Level,
    string[]? BackupIds,
    string[] RestoreRoots);

/// <summary>
/// The request to issue an MCP token
/// </summary>
/// <param name="Name">The name of the token, usually the agent it is for</param>
/// <param name="Level">What the token allows</param>
/// <param name="BackupIds">The IDs of the backups the token can be used with, or <c>null</c> for all backups</param>
/// <param name="RestoreRoots">The folders the token can restore into, besides the original location of the files, or <c>null</c> for none</param>
/// <param name="ExpiresIn">How long the token is valid, as a Duplicati time span such as 90D or 1Y, or <c>null</c> for 90 days</param>
public sealed record CreateMcpTokenInputDto(
    string Name,
    McpTokenLevel Level,
    string[]? BackupIds,
    string[]? RestoreRoots,
    string? ExpiresIn);

/// <summary>
/// The result of issuing an MCP token
/// </summary>
/// <param name="Token">The description of the token</param>
/// <param name="AccessToken">The token to give to the agent. It cannot be retrieved again.</param>
public sealed record CreateMcpTokenOutputDto(McpTokenDto Token, string AccessToken);
