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
/// Represents the response received after uploading a file to Backblaze B2.
/// Contains details about the uploaded file including its ID, name, and metadata.
/// </summary>
internal class UploadFileResponse : AccountIDEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the bucket containing the file.
    /// </summary>
    [JsonProperty("bucketId")]
    public string BucketID { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier assigned to the uploaded file.
    /// </summary>
    [JsonProperty("fileId")]
    public string FileID { get; set; }

    /// <summary>
    /// Gets or sets the name of the uploaded file.
    /// </summary>
    [JsonProperty("fileName")]
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the size of the uploaded file in bytes.
    /// </summary>
    [JsonProperty("contentLength")]
    public long ContentLength { get; set; }

    /// <summary>
    /// Gets or sets the SHA1 hash of the file content.
    /// </summary>
    [JsonProperty("contentSha1")]
    public string ContentSha1 { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the uploaded file.
    /// </summary>
    [JsonProperty("contentType")]
    public string ContentType { get; set; }
}