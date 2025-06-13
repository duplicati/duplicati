// Copyright (C) 2025, The Duplicati Team
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
/// The fileset DTO
/// </summary>
public sealed record ListFilesetsResponseDto : PagedResponseEnvelope<ListFilesetsResponseItem>
{
    /// <summary>
    /// Creates a new instance of the ListFilesetsResponseDto with the given error and status code
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <returns>The ListFilesetsResponseDto instance</returns>
    public static new ListFilesetsResponseDto Failure(
        string error,
        string statusCode
    ) => new ListFilesetsResponseDto()
    {
        Success = false,
        Error = error,
        StatusCode = statusCode,
        Data = null,
        PageInfo = null
    };

    /// <summary>
    /// Creates a new instance of the ListFilesetsResponseDto with the given filesets
    /// </summary>
    /// <param name="filesets">The filesets to include in the response</param>
    /// <returns>The ListFilesetsResponseDto instance</returns>
    public static ListFilesetsResponseDto Create(
        IEnumerable<ListFilesetsResponseItem> filesets
    ) => new ListFilesetsResponseDto()
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
/// The fileset DTO
/// </summary>
public sealed record ListFilesetsResponseItem
{
    /// <summary>
    /// The fileset version
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// The fileset time
    /// </summary>
    public required DateTime Time { get; init; }

    /// <summary>
    /// A flag indicating if the fileset is a full backup
    /// </summary>
    public required bool? IsFullBackup { get; init; }

    /// <summary>
    /// The number of files in the fileset
    /// </summary>
    public required long? FileCount { get; init; }

    /// <summary>
    /// The total size of the fileset
    /// </summary>
    public required long? FileSizes { get; init; }
}