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
/// File or folder entry in Drime Cloud
/// </summary>
public class FileEntry
{
    /// <summary>
    /// Unique entry ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Entry name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Entry type (file, folder, image, text, audio, video, pdf)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Unique hash for the entry (used for downloads)
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes (0 for folders)
    /// </summary>
    public long File_Size { get; set; }

    /// <summary>
    /// MIME type
    /// </summary>
    public string? Mime { get; set; }

    /// <summary>
    /// File extension
    /// </summary>
    public string? Extension { get; set; }

    /// <summary>
    /// Parent folder ID (null for root)
    /// </summary>
    public long? Parent_Id { get; set; }

    /// <summary>
    /// Workspace ID
    /// </summary>
    public long Workspace_Id { get; set; }

    /// <summary>
    /// URL for previewing the file
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Thumbnail URL (for images/videos)
    /// </summary>
    public string? Thumbnail_Url { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public string Created_At { get; set; } = string.Empty;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public string Updated_At { get; set; } = string.Empty;

    /// <summary>
    /// Deletion timestamp (null if not deleted)
    /// </summary>
    public string? Deleted_At { get; set; }
}

/// <summary>
/// Response wrapper for file entry operations
/// </summary>
public class FileEntryResponse
{
    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if any
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The file entry
    /// </summary>
    public FileEntry? FileEntry { get; set; }
}

/// <summary>
/// Response wrapper for folder operations
/// </summary>
public class FolderEntryResponse
{
    /// <summary>
    /// Response status
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if any
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The folder entry
    /// </summary>
    public FileEntry? Folder { get; set; }
}

/// <summary>
/// Generic response envelope
/// </summary>
public class ResponseEnvelope
{
    /// <summary>
    /// Response status
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Error message if any
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Error response from API
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Response status
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Error message
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Validation errors by field
    /// </summary>
    public Dictionary<string, string>? Errors { get; set; }
}
