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

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// An interface a backend may implement when it can create a hard link: a second
/// remote name for the content of an existing remote file, without storing the
/// content again. Snapshot-style sync uses it to keep an unchanged file in a new
/// snapshot folder without uploading it again.
/// </summary>
/// <remarks>
/// Implementing the interface says that the backend type can link, not that every
/// destination it points at can. A filesystem without hard links (FAT32, exFAT, many
/// network shares) makes <see cref="CreateHardLinkAsync"/> throw
/// <see cref="HardLinkNotSupportedException"/>; callers then fall back to uploading the
/// content again.
/// </remarks>
public interface ILinkEnabledBackend : IBackend
{
    /// <summary>
    /// Creates a hard link at <paramref name="targetName"/> that points at the content of
    /// the existing remote file <paramref name="existingName"/>. Both names are relative
    /// to the backend root and may contain sub-folders separated by '/'. The folder that
    /// holds <paramref name="targetName"/> must exist, and <paramref name="targetName"/>
    /// itself must not.
    /// </summary>
    /// <param name="existingName">The remote name of the existing file, relative to the root</param>
    /// <param name="targetName">The remote name of the link to create, relative to the root</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="HardLinkNotSupportedException">The destination cannot create hard links</exception>
    Task CreateHardLinkAsync(string existingName, string targetName, CancellationToken cancellationToken);
}
