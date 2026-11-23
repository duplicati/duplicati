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
/// The request for sending a test email
/// </summary>
public sealed record TestEmailRequestDto
{
    /// <summary>
    /// The send-mail-* options to use, with or without leading dashes.
    /// Other options are ignored. A value that is the password placeholder
    /// is replaced with the stored value, from the backup if <see cref="BackupId"/> is set,
    /// otherwise from the global settings.
    /// </summary>
    public Dictionary<string, string?>? Options { get; init; }

    /// <summary>
    /// The id of a stored backup, used to fill in masked passwords
    /// </summary>
    public string? BackupId { get; init; }

    /// <summary>
    /// If set, send-mail-* options in the global settings are used when
    /// they are not in <see cref="Options"/>, as they are when a backup runs
    /// </summary>
    public bool? IncludeGlobalOptions { get; init; }
}

/// <summary>
/// The response for sending a test email
/// </summary>
public sealed record TestEmailResponseDto : ResponseEnvelope<TestEmailResult>
{
    /// <summary>
    /// Creates a success response
    /// </summary>
    /// <param name="log">The messages logged while sending</param>
    /// <returns>The response DTO</returns>
    public static TestEmailResponseDto Create(IEnumerable<string> log)
        => new()
        {
            Success = true,
            Error = null,
            StatusCode = "OK",
            Data = new TestEmailResult { Log = log.ToArray() }
        };

    /// <summary>
    /// Creates a failure response
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <param name="log">The messages logged while sending</param>
    /// <returns>The response DTO</returns>
    public static TestEmailResponseDto CreateFailure(string error, string statusCode, IEnumerable<string>? log = null)
        => new()
        {
            Success = false,
            Error = error,
            StatusCode = statusCode,
            Data = new TestEmailResult { Log = log?.ToArray() ?? [] }
        };
}

/// <summary>
/// The result of sending a test email
/// </summary>
public sealed record TestEmailResult
{
    /// <summary>
    /// The messages logged while sending, such as the server used or servers that failed
    /// </summary>
    public required string[] Log { get; init; }
}
