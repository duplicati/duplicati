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

namespace Duplicati.Library.Backend.DrimeCloud.Model;

/// <summary>
/// Response from creating a multipart upload
/// </summary>
public class CreateMultipartResponse
{
    /// <summary>
    /// S3 key for the upload
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Multipart upload ID
    /// </summary>
    public string UploadId { get; set; } = string.Empty;

    /// <summary>
    /// Access control
    /// </summary>
    public string Acl { get; set; } = string.Empty;

    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Response from signing part URLs
/// </summary>
public class SignPartUrlsResponse
{
    /// <summary>
    /// Array of signed URLs
    /// </summary>
    public List<SignedPartUrl> Urls { get; set; } = new();

    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Signed URL for a part upload
/// </summary>
public class SignedPartUrl
{
    /// <summary>
    /// Part number
    /// </summary>
    public int PartNumber { get; set; }

    /// <summary>
    /// Presigned URL for PUT upload
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Response from completing a multipart upload
/// </summary>
public class CompleteMultipartResponse
{
    /// <summary>
    /// Final S3 location
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Response from creating an S3 entry
/// </summary>
public class CreateS3EntryResponse
{
    /// <summary>
    /// The created file entry
    /// </summary>
    public FileEntry? FileEntry { get; set; }

    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if any
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Part info for completing multipart upload
/// </summary>
public class MultipartPart
{
    /// <summary>
    /// Part number (1-indexed)
    /// </summary>
    public int PartNumber { get; set; }

    /// <summary>
    /// ETag from S3 upload response (with quotes)
    /// </summary>
    public string ETag { get; set; } = string.Empty;
}
