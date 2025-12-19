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

internal class FileRetention
{
    [JsonProperty("mode")]
    public string? Mode { get; set; }

    [JsonProperty("retainUntilTimestamp")]
    public long? RetainUntilTimestamp { get; set; }
}

internal class FileRetentionAccess
{
    [JsonProperty("isClientAuthorizedToRead")]
    public bool isClientAuthorizedToRead { get; set; }

    [JsonProperty("value")]
    public FileRetention? Value { get; set; }

}

internal class GetFileInfoRequest
{
    [JsonProperty("fileId")]
    public string? FileId { get; set; }
}

internal class GetFileInfoResponse
{
    [JsonProperty("fileRetention")]
    public FileRetentionAccess? FileRetention { get; set; }
}

internal class UpdateFileRetentionResponse
{
    [JsonProperty("fileRetention")]
    public FileRetention? FileRetention { get; set; }
}


internal class UpdateFileRetentionRequest
{
    [JsonProperty("fileId")]
    public string? FileId { get; set; }

    [JsonProperty("fileName")]
    public string? FileName { get; set; }

    [JsonProperty("fileRetention")]
    public FileRetention? FileRetention { get; set; }

    [JsonProperty("bypassGovernance")]
    public bool BypassGovernance { get; set; }
}
