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
/// The response DTO for listing broken files in a backup.
/// </summary>
public sealed record ListBrokenFilesResponseDto : PagedResponseEnvelope<ListBrokenFilesFilesetItem>
{
    /// <summary>
    /// Creates a new instance of the ListBrokenFilesResponseDto with the given error and status code.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <returns>The ListBrokenFilesResponseDto instance</returns>
    public static new ListBrokenFilesResponseDto Failure(
        string error,
        string statusCode
    ) => new ListBrokenFilesResponseDto()
    {
        Success = false,
        Error = error,
        StatusCode = statusCode,
        Data = null,
        PageInfo = null
    };

    /// <summary>
    /// Creates a new instance of the ListBrokenFilesResponseDto with the given broken filesets.
    /// </summary>
    /// <param name="filesets">The broken filesets to include in the response</param>
    /// <returns>The ListBrokenFilesResponseDto instance</returns>
    public static ListBrokenFilesResponseDto Create(
        IEnumerable<ListBrokenFilesFilesetItem> filesets
    ) => new ListBrokenFilesResponseDto()
    {
        Success = true,
        Error = null,
        StatusCode = "OK",
        Data = filesets,
        PageInfo = new PageInfo()
        {
            Total = filesets.Count(),
            Page = 1,
            Pages = 1,
            PageSize = filesets.Count(),
        }
    };
}

/// <summary>
/// A fileset containing broken files.
/// </summary>
public sealed record ListBrokenFilesFilesetItem
{
    /// <summary>
    /// The fileset ID.
    /// </summary>
    public required long FilesetID { get; init; }

    /// <summary>
    /// The fileset time.
    /// </summary>
    public required DateTime FilesetTime { get; init; }

    /// <summary>
    /// The broken files within the fileset.
    /// </summary>
    public required IEnumerable<ListBrokenFilesFileItem> Files { get; init; }
}

/// <summary>
/// A single broken file entry.
/// </summary>
public sealed record ListBrokenFilesFileItem
{
    /// <summary>
    /// The path of the broken file.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The size of the broken file.
    /// </summary>
    public required long Size { get; init; }
}
