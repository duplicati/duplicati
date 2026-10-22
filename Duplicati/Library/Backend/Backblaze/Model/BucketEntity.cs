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

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Backblaze.Model;

/// <summary>
/// Represents a bucket entity in the Backblaze B2 storage system.
/// Contains information about a storage bucket including its identifier and type.
/// </summary>
internal class BucketEntity : AccountIDEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the bucket.
    /// This property can be null as specified by the NullValueHandling attribute.
    /// </summary>
    [JsonProperty("bucketId", NullValueHandling = NullValueHandling.Ignore)]
    public string? BucketID { get; set; }

    /// <summary>
    /// Gets or sets the name of the bucket.
    /// </summary>
    [JsonProperty("bucketName")]
    public required string BucketName { get; set; }

    /// <summary>
    /// Gets or sets the type of the bucket (e.g., "allPrivate", "allPublic").
    /// </summary>
    [JsonProperty("bucketType")]
    public string? BucketType { get; set; }

    /// <summary>
    /// Gets or sets the file lock (object lock) configuration of the bucket.
    /// </summary>
    [JsonProperty("fileLockConfiguration", NullValueHandling = NullValueHandling.Ignore)]
    public FileLockConfigurationEntity? FileLockConfiguration { get; set; }
}

/// <summary>
/// The file lock configuration wrapper returned by b2_list_buckets
/// </summary>
internal class FileLockConfigurationEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether the caller is authorized to read the configuration.
    /// </summary>
    [JsonProperty("isClientAuthorizedToRead")]
    public bool? IsClientAuthorizedToRead { get; set; }

    /// <summary>
    /// Gets or sets the configuration value, which is <c>null</c> if the caller is not authorized to read it.
    /// </summary>
    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public FileLockConfigurationValueEntity? Value { get; set; }
}

/// <summary>
/// The file lock configuration of a bucket
/// </summary>
internal class FileLockConfigurationValueEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether file lock is enabled for the bucket.
    /// </summary>
    [JsonProperty("isFileLockEnabled")]
    public bool? IsFileLockEnabled { get; set; }
}