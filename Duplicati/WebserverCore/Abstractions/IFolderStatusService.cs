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

using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Service for querying folder backup status.
/// Used by the Windows Shell Extension to show overlay icons on backed up folders.
/// </summary>
public interface IFolderStatusService
{
    /// <summary>
    /// Gets the backup status for all source folders across all backups
    /// </summary>
    /// <returns>List of folder status information</returns>
    IEnumerable<FolderStatusDto> GetAllFolderStatuses();

    /// <summary>
    /// Gets the backup status for a specific folder path
    /// </summary>
    /// <param name="path">The folder path to check</param>
    /// <returns>The folder status information</returns>
    FolderStatusDto GetFolderStatus(string path);
}
