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
/// Represents the response from a list files operation in Backblaze B2.
/// Contains a list of files and pagination information for subsequent requests.
/// </summary>
internal class ListFilesResponse
{
    /// <summary>
    /// Gets or sets the name of the next file to use for pagination.
    /// This value should be used in the next request to continue listing files.
    /// </summary>
    [JsonProperty("nextFileName")]
    public string NextFileName { get; set; }

    /// <summary>
    /// Gets or sets the ID of the next file to use for pagination.
    /// This value should be used in conjunction with NextFileName for subsequent requests.
    /// </summary>
    [JsonProperty("nextFileId")]
    public string NextFileID { get; set; }

    /// <summary>
    /// Gets or sets the array of file entities returned by this request.
    /// Each element contains information about a single file in the bucket.
    /// </summary>
    [JsonProperty("files")]
    public FileEntity[] Files { get; set; }
}