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
/// Information returned for a file when listed directly by UUID
/// </summary>
internal class FileMetadataLong
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = string.Empty;

    [JsonPropertyName("nameEncrypted")]
    public string NameEncrypted { get; set; } = string.Empty;

    [JsonPropertyName("nameHashed")]
    public string NameHashed { get; set; } = string.Empty;
    [JsonPropertyName("sizeEncrypted")]
    public string SizeEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("mimeEncrypted")]
    public string MimeEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("metadata")]
    public string MetadataEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("size")]
    public long Size { get; set; }
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;
    [JsonPropertyName("versioned")]
    public bool Versioned { get; set; }
    [JsonPropertyName("trash")]
    public bool Thrash { get; set; }
    [JsonPropertyName("version")]
    public int Version { get; set; }
    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }
}

