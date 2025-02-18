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
/// Represents a request to delete a file from Backblaze B2.
/// Contains the necessary information to identify the file to be deleted.
/// </summary>
internal class DeleteRequest
{
    /// <summary>
    /// Gets or sets the name of the file to be deleted.
    /// This is the full path of the file in the bucket.
    /// </summary>
    [JsonProperty("fileName")]
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the file to be deleted.
    /// This ID is assigned by Backblaze B2 when the file is uploaded.
    /// </summary>
    [JsonProperty("fileId")]
    public string FileId { get; set; }
}