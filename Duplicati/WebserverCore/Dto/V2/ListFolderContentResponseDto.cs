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
/// The list folder content item DTO
/// </summary>
public sealed record ListFolderContentItemDto
{
    /// <summary>
    /// The path of the entry
    /// </summary>
    public required string Path { get; init; }
    /// <summary>
    /// The size of the entry
    /// </summary>
    public required long Size { get; init; }
    /// <summary>
    /// True if the entry is a directory, false otherwise
    /// </summary>
    public required bool IsDirectory { get; init; }
    /// <summary>
    /// True if the entry is a symlink, false otherwise
    /// </summary>
    public required bool IsSymlink { get; init; }
    /// <summary>
    /// The last modified time of the entry
    /// </summary>
    public required DateTime LastModified { get; init; }
}

/// <summary>
/// The list folder content response DTO
/// </summary>
public sealed record ListFolderContentResponseDto : PagedResponseEnvelope<ListFolderContentItemDto>
{
    /// <summary>
    /// Creates a new instance of the <see cref="ListFolderContentResponseDto"/> class
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <returns>A new instance of the <see cref="ListFolderContentResponseDto"/> class</returns>
    public static new ListFolderContentResponseDto Failure(string error, string statusCode) =>
        new ListFolderContentResponseDto
        {
            Success = false,
            Error = error,
            StatusCode = statusCode,
            Data = null,
            PageInfo = null
        };

    /// <summary>
    /// Creates a new instance of the <see cref="ListFolderContentResponseDto"/> class
    /// </summary>
    /// <param name="items">The result items</param>
    /// <param name="page">The page of the result</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="totalCount">The total count of items</param>
    /// <returns>A new instance of the <see cref="ListFolderContentResponseDto"/> class</returns>
    public static ListFolderContentResponseDto Create(
        IEnumerable<ListFolderContentItemDto> items,
        int page,
        int pageSize,
        long totalCount)
    {
        return new ListFolderContentResponseDto
        {
            Data = items,
            Success = true,
            StatusCode = "OK",
            Error = null,
            PageInfo = new PageInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                Pages = (int)Math.Ceiling((double)totalCount / pageSize)
            }
        };
    }
}