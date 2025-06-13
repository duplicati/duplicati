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
/// The list filesets request DTO
/// </summary>
public sealed record DestinationTestResponseDto : ResponseEnvelope<DestinationTestResult>
{
    /// <summary>
    /// Creates a new instance of the DestinationTestResponseDto with the given error and status code
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="statusCode">The status code</param>
    /// <param name="folderExists">Flag indicating if the folder exists</param>
    /// <param name="afterConnect">Flag indicating if the connection was made and authenticated</param>
    /// <param name="hostCertificate">The host certificate, if any</param>
    /// <param name="reportedHostKey">The reported host key, if any</param>
    /// <param name="acceptedHostKey">The currently accepted host key, if any</param>
    /// <returns>The DestinationTestResponseDto instance</returns>
    public static DestinationTestResponseDto Failure(
        string error,
        string statusCode,
        bool? folderExists = null,
        bool? afterConnect = null,
        string? hostCertificate = null,
        string? reportedHostKey = null,
        string? acceptedHostKey = null
    ) => new DestinationTestResponseDto()
    {
        Success = false,
        Error = error,
        StatusCode = statusCode,
        Data = hostCertificate == null && reportedHostKey == null && acceptedHostKey == null && afterConnect == null
            ? null
            : new DestinationTestResult()
            {
                FolderExists = folderExists,
                AfterConnect = afterConnect,
                FolderIsEmpty = null,
                FolderContainsBackupFiles = null,
                FolderContainsEncryptedBackupFiles = null,
                HostCertificate = hostCertificate,
                ReportedHostKey = reportedHostKey,
                AcceptedHostKey = acceptedHostKey
            }
    };

    /// <summary>
    /// Creates a new instance of the DestinationTestResponseDto with the given error and status code
    /// </summary>
    /// <param name="anyFiles">Flag indicating if there are any files</param>
    /// <param name="anyBackups">Flag indicating if there are any backups</param>
    /// <param name="anyEncryptedFiles">Flag indicating if there are any encrypted files</param>
    /// <returns>The DestinationTestResponseDto instance</returns>
    public static DestinationTestResponseDto Create(bool anyFiles, bool anyBackups, bool anyEncryptedFiles)
    {
        return new DestinationTestResponseDto()
        {
            Success = true,
            Error = null,
            StatusCode = "OK",
            Data = new DestinationTestResult()
            {
                FolderExists = true,
                AfterConnect = true,
                FolderIsEmpty = !anyFiles,
                FolderContainsBackupFiles = anyBackups,
                FolderContainsEncryptedBackupFiles = anyEncryptedFiles,
                HostCertificate = null,
                ReportedHostKey = null,
                AcceptedHostKey = null
            }
        };
    }
}

/// <summary>
/// The destination test result DTO
/// </summary>
public sealed record DestinationTestResult
{
    /// <summary>
    /// Flag indicating if the folder exists
    /// </summary>
    public required bool? FolderExists { get; init; }
    /// <summary>
    /// Flag indicating if the folder is empty
    /// </summary>
    public required bool? FolderIsEmpty { get; init; }
    /// <summary>
    /// Flag indicating if the folder contains backup files
    /// </summary>
    public required bool? FolderContainsBackupFiles { get; init; }
    /// <summary>
    /// Flag indicating if the folder contains encrypted backup files
    /// </summary>
    public required bool? FolderContainsEncryptedBackupFiles { get; init; }
    /// <summary>
    /// Flag indicating if the connection was made and authenticated
    /// </summary>
    public required bool? AfterConnect { get; init; }
    /// <summary>
    /// The host certificate, if any
    /// </summary>
    public required string? HostCertificate { get; init; }
    /// <summary>
    /// The reported host key, if any
    /// </summary>
    public required string? ReportedHostKey { get; init; }
    /// <summary>
    /// The currently accepted host key, if any
    /// </summary>
    public required string? AcceptedHostKey { get; init; }
}