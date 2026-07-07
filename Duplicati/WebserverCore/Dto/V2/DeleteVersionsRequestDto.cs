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
namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// The request DTO for deleting specific backup versions.
/// </summary>
public sealed record DeleteVersionsRequestDto
{
    /// <summary>
    /// The backup ID to delete versions from.
    /// </summary>
    public required string BackupId { get; init; }

    /// <summary>
    /// The versions to delete. Each version is a non-negative integer
    /// corresponding to a fileset version reported by the list-filesets endpoint.
    /// </summary>
    public required long[] Versions { get; init; }

    /// <summary>
    /// A flag indicating if the last remaining fileset may be removed.
    /// If not set, the last fileset is always kept.
    /// </summary>
    public bool? AllowFullRemoval { get; init; }

    /// <summary>
    /// A flag indicating whether to suppress automatic compaction after deleting versions.
    /// </summary>
    public bool? SuppressCompact { get; init; }
}
