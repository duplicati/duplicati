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

using System.Text.Json.Serialization;

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// The raw file entry as returned by the Filen API
/// </summary>
internal class FolderListEntry
{
    /// <summary>
    /// The UUID of the file or folder
    /// </summary>
    [JsonPropertyName("uuid")]
    public string UUID { get; set; } = string.Empty;

    /// <summary>
    /// The encrypted name of the file or folder
    /// </summary>
    [JsonPropertyName("name")]
    public string EncryptedName { get; set; } = string.Empty;

    /// <summary>
    /// The size of the file in bytes, or 0 if the entry is a folder
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// The last modified date of the file or folder
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long LastModified { get; set; }

    /// <summary>
    /// The encrypted metadata of the file
    /// </summary>
    [JsonPropertyName("metadata")]
    public string EncryptedMetadata { get; set; } = string.Empty;
}

