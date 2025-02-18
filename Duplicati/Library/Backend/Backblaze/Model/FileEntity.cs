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

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Backblaze.Model;

/// <summary>
/// Represents a file entity in the Backblaze B2 storage system.
/// Contains detailed information about a file including its identifier, name, size, and metadata.
/// </summary>
internal class FileEntity
{
    /// <summary>
    /// Gets or sets the unique identifier assigned to the file by Backblaze B2.
    /// This ID is used to reference the file in various API operations.
    /// </summary>
    [JsonProperty("fileId")]
    public string FileID { get; set; }

    /// <summary>
    /// Gets or sets the name of the file as stored in Backblaze B2.
    /// This is the full path of the file within the bucket.
    /// </summary>
    [JsonProperty("fileName")]
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the action performed on the file.
    /// Possible values include "upload" for new files, "hide" for hidden files,
    /// and "delete" for deleted files.
    /// </summary>
    [JsonProperty("action")]
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets the size of the file in bytes.
    /// Represents the actual content length of the file.
    /// </summary>
    [JsonProperty("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the file was uploaded.
    /// Value is in milliseconds since epoch (January 1, 1970 00:00:00 UTC).
    /// </summary>
    [JsonProperty("uploadTimestamp")]
    public long UploadTimestamp { get; set; }
}