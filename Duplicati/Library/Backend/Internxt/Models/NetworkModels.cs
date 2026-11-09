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
using System.Text.Json.Serialization;

namespace Duplicati.Library.Backend.Internxt.Models;

/// <summary>
/// Request body for POST /network/v2/buckets/{bucket}/files/start
/// </summary>
public sealed class StartUploadRequest
{
    [JsonPropertyName("uploads")]
    public List<StartUploadPart> Uploads { get; set; } = [];
}

/// <summary>
/// One entry in the start upload request
/// </summary>
public sealed class StartUploadPart
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// Response from POST /network/v2/buckets/{bucket}/files/start
/// </summary>
public sealed class StartUploadResponse
{
    [JsonPropertyName("uploads")]
    public List<UploadTarget> Uploads { get; set; } = [];
}

/// <summary>
/// An upload target handed out by the network
/// </summary>
public sealed class UploadTarget
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// The shard uuid to report back in the finish call
    /// </summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>
    /// The pre-signed URL for a single-part upload
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// The pre-signed URLs for a multipart upload, one per part
    /// </summary>
    [JsonPropertyName("urls")]
    public List<string>? Urls { get; set; }

    /// <summary>
    /// The multipart upload id
    /// </summary>
    [JsonPropertyName("UploadId")]
    public string? UploadId { get; set; }
}

/// <summary>
/// Request body for POST /network/v2/buckets/{bucket}/files/finish
/// </summary>
public sealed class FinishUploadRequest
{
    /// <summary>
    /// The hex encoded file index
    /// </summary>
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    [JsonPropertyName("shards")]
    public List<FinishShard> Shards { get; set; } = [];
}

/// <summary>
/// A shard in the finish request
/// </summary>
public sealed class FinishShard
{
    /// <summary>
    /// The RIPEMD-160(SHA-256) of the whole ciphertext
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("UploadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UploadId { get; set; }

    [JsonPropertyName("parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CompletedPart>? Parts { get; set; }
}

/// <summary>
/// A completed multipart part
/// </summary>
public sealed class CompletedPart
{
    [JsonPropertyName("PartNumber")]
    public int PartNumber { get; set; }

    [JsonPropertyName("ETag")]
    public string ETag { get; set; } = string.Empty;
}

/// <summary>
/// Response from the finish call
/// </summary>
public sealed class FinishUploadResponse
{
    /// <summary>
    /// The network file id
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public string? Index { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

/// <summary>
/// Response from GET /network/buckets/{bucket}/files/{fileId}/info
/// </summary>
public sealed class FileInfoResponse
{
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("shards")]
    public List<DownloadShard> Shards { get; set; } = [];
}

/// <summary>
/// A downloadable shard
/// </summary>
public sealed class DownloadShard
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
