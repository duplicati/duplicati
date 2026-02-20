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
/// The temporary disk space response DTO
/// </summary>
public sealed record TempDiskSpaceResponseDto : ResponseEnvelope<TempDiskSpaceResult>
{
    /// <summary>
    /// Creates a success response with the temp disk space information
    /// </summary>
    /// <param name="tempPath">The path to the temporary folder</param>
    /// <param name="freeSpace">The free space in bytes</param>
    /// <param name="totalSpace">The total space in bytes</param>
    /// <returns>The response DTO</returns>
    public static TempDiskSpaceResponseDto Create(string tempPath, long? freeSpace, long? totalSpace, long dblockSize, long restoreCacheMax)
    {
        return new TempDiskSpaceResponseDto()
        {
            Success = true,
            Error = null,
            StatusCode = "OK",
            Data = new TempDiskSpaceResult()
            {
                TempPath = tempPath,
                FreeSpace = freeSpace,
                TotalSpace = totalSpace,
                DblockSize = dblockSize,
                RestoreCacheMax = restoreCacheMax
            }
        };
    }

    /// <summary>
    /// Creates a failure response
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <returns>The response DTO</returns>
    public static new TempDiskSpaceResponseDto Failure(string error, string statusCode)
    {
        return new TempDiskSpaceResponseDto()
        {
            Success = false,
            Error = error,
            StatusCode = statusCode,
            Data = null
        };
    }
}

/// <summary>
/// The temporary disk space result
/// </summary>
public sealed record TempDiskSpaceResult
{
    /// <summary>
    /// The path to the temporary folder that was checked
    /// </summary>
    public required string TempPath { get; init; }

    /// <summary>
    /// The free space available in bytes, or null if it could not be determined
    /// </summary>
    public required long? FreeSpace { get; init; }

    /// <summary>
    /// The total space of the drive in bytes, or null if it could not be determined
    /// </summary>
    public required long? TotalSpace { get; init; }

    /// <summary>
    /// The dblock-size setting in bytes
    /// </summary>
    public required long DblockSize { get; init; }

    /// <summary>
    /// The restore-cache-max setting in bytes
    /// </summary>
    public required long RestoreCacheMax { get; init; }
}
