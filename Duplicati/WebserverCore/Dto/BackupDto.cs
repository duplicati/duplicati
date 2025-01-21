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
namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The backup DTO
/// </summary>
public sealed record BackupDto
{
    /// <summary>
    /// The backup ID
    /// </summary>
    public required string ID { get; init; }
    /// <summary>
    /// The backup name
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The backup description
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// The backup tags
    /// </summary>
    public required string[] Tags { get; init; }
    /// <summary>
    /// The backup target url
    /// </summary>
    public required string TargetURL { get; init; }
    /// <summary>
    /// The path to the local database
    /// </summary>
    public required string DBPath { get; init; }

    /// <summary>
    /// The backup source folders and files
    /// </summary>
    public required string[] Sources { get; init; }

    /// <summary>
    /// The backup settings
    /// </summary>
    public required IEnumerable<SettingDto>? Settings { get; init; }

    /// <summary>
    /// The filters applied to the source files
    /// </summary>
    public required IEnumerable<FilterDto>? Filters { get; init; }

    /// <summary>
    /// The backup metadata
    /// </summary>
    public required IDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets a value indicating if this instance is not persisted to the database
    /// </summary>
    public required bool IsTemporary { get; init; }

    /// <summary>
    /// Gets a value indicating if backup is unencrypted or passphrase is stored
    /// </summary>
    public required bool IsUnencryptedOrPassphraseStored { get; init; }
}
