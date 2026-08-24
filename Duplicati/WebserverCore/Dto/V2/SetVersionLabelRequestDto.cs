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
namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// The request DTO for updating the label of a backup version.
/// </summary>
public sealed record SetVersionLabelRequestDto
{
    /// <summary>
    /// The backup ID to update the version label for.
    /// </summary>
    public required string BackupId { get; init; }

    /// <summary>
    /// The version to update, as reported by the list-filesets endpoint.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// The label to assign to the version, or null/empty to clear the label.
    /// The label is stored in the local database and included in the
    /// labels.json file of the next backup.
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// The response DTO for updating the label of a backup version.
/// </summary>
public sealed record SetVersionLabelResponseDto
{
    /// <summary>
    /// The version that was updated.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// The timestamp of the version that was updated.
    /// </summary>
    public required DateTime Time { get; init; }

    /// <summary>
    /// The label that was assigned, or null if the label was cleared.
    /// </summary>
    public required string? Label { get; init; }
}
