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
/// DTO for the result of testing a single filesystem path against filters
/// </summary>
public record TestFilterResponseItem
{
    /// <summary>
    /// The path that was evaluated
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Whether the path is included by the filters
    /// </summary>
    public bool Included { get; set; }

    /// <summary>
    /// The string representation of the filter that matched, if any
    /// </summary>
    public string? MatchedFilter { get; set; }
}

/// <summary>
/// DTO for the response of testing filesystem filters
/// </summary>
public record TestFilterResponseDto : ResponseEnvelope<TestFilterResponseItem[]>
{
    /// <summary>
    /// Creates a success response with the test results
    /// </summary>
    /// <param name="data">The data to include</param>
    /// <returns>The response DTO</returns>
    public static TestFilterResponseDto Create(TestFilterResponseItem[] data)
        => new TestFilterResponseDto()
        {
            Success = true,
            Error = null,
            StatusCode = "OK",
            Data = data
        };

    /// <summary>
    /// Creates an error response
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <returns>The response DTO</returns>
    public static TestFilterResponseDto CreateError(string error, string statusCode)
        => new TestFilterResponseDto()
        {
            Success = false,
            Error = error,
            StatusCode = statusCode,
            Data = null
        };
}
