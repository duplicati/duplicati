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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Duplicati.Library.Common.IO;

/// <summary>
/// Helper class for working with file attributes not listed in <see cref="FileAttributes"/>
/// </summary>
public static class ExtendedFileAttributes
{
    /// <summary>
    /// Comprehensive mapping of Windows FILE_ATTRIBUTE_* constants from winnt.h
    /// </summary>
    [SupportedOSPlatform("windows")]
    [Flags]
    public enum Win32FileAttributes : uint
    {
        /// <summary>No attributes set.</summary>
        None = 0x00000000,

        /// <summary>FILE_ATTRIBUTE_READONLY: File is read-only.</summary>
        ReadOnly = 0x00000001,

        /// <summary>FILE_ATTRIBUTE_HIDDEN: File or directory is hidden.</summary>
        Hidden = 0x00000002,

        /// <summary>FILE_ATTRIBUTE_SYSTEM: Part of or used exclusively by the OS.</summary>
        System = 0x00000004,

        /// <summary>FILE_ATTRIBUTE_DIRECTORY: Identifies a directory handle.</summary>
        Directory = 0x00000010,

        /// <summary>FILE_ATTRIBUTE_ARCHIVE: Marked for backup or removal.</summary>
        Archive = 0x00000020,

        /// <summary>FILE_ATTRIBUTE_DEVICE: Reserved for system use.</summary>
        Device = 0x00000040,

        /// <summary>FILE_ATTRIBUTE_NORMAL: No other attributes set (only valid when used alone).</summary>
        Normal = 0x00000080,

        /// <summary>FILE_ATTRIBUTE_TEMPORARY: Used for temporary storage; cached heavily.</summary>
        Temporary = 0x00000100,

        /// <summary>FILE_ATTRIBUTE_SPARSE_FILE: File is a sparse file.</summary>
        SparseFile = 0x00000200,

        /// <summary>FILE_ATTRIBUTE_REPARSE_POINT: Has an associated reparse point or symlink.</summary>
        ReparsePoint = 0x00000400,

        /// <summary>FILE_ATTRIBUTE_COMPRESSED: All data in the file/directory is compressed.</summary>
        Compressed = 0x00000800,

        /// <summary>FILE_ATTRIBUTE_OFFLINE: Data physically moved to offline storage.</summary>
        Offline = 0x00001000,

        /// <summary>FILE_ATTRIBUTE_NOT_CONTENT_INDEXED: Will not be indexed by content indexing services.</summary>
        NotContentIndexed = 0x00002000,

        /// <summary>FILE_ATTRIBUTE_ENCRYPTED: File or directory is encrypted.</summary>
        Encrypted = 0x00004000,

        /// <summary>FILE_ATTRIBUTE_INTEGRITY_STREAM: ReFS directory/stream configured with integrity.</summary>
        IntegrityStream = 0x00008000,

        /// <summary>FILE_ATTRIBUTE_VIRTUAL: Reserved for internal system use.</summary>
        Virtual = 0x00010000,

        /// <summary>FILE_ATTRIBUTE_NO_SCRUB_DATA: Stream skipped by background data integrity scanner.</summary>
        NoScrubData = 0x00020000,

        /// <summary>FILE_ATTRIBUTE_EA: File/directory has extended attributes (Internal use only).</summary>
        ExtendedAttributes = 0x00040000,

        /// <summary>FILE_ATTRIBUTE_RECALL_ON_OPEN: Completely virtual, has no local physical representation.</summary>
        RecallOnOpen = 0x00040000,

        /// <summary>FILE_ATTRIBUTE_PINNED: Cloud file system user intent to keep fully present locally.</summary>
        Pinned = 0x00080000,

        /// <summary>FILE_ATTRIBUTE_UNPINNED: Cloud file system intent to NOT keep present unless active.</summary>
        Unpinned = 0x00100000,

        /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: Not fully present locally; reading will fetch from cloud.</summary>
        RecallOnDataAccess = 0x00400000
    }

    /// <summary>
    /// Merge the default C# FileAttributes with the Win32FileAttributes and return the union.
    /// </summary>
    private static Lazy<Dictionary<string, uint>> _nameToValueMap = new Lazy<Dictionary<string, uint>>(() =>
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var included = new HashSet<uint>();

        // Add the built-in values
        foreach (var name in Enum.GetNames<FileAttributes>())
        {
            map[name] = (uint)Enum.Parse<FileAttributes>(name);
            included.Add(map[name]);
        }

        if (OperatingSystem.IsWindows())
        {
            // Merge in the Win32FileAttributes, but prefer the built-in values
            foreach (var name in Enum.GetNames<Win32FileAttributes>())
            {
                if (map.ContainsKey(name))
                    continue;

                var value = (uint)Enum.Parse<Win32FileAttributes>(name);
                if (included.Contains(value))
                    continue;

                map[name] = value;
            }
        }

        return map;
    });

    /// <summary>
    /// Get the names of all known file attributes, including both the built-in and Win32 values.
    /// </summary>
    /// <returns>The list of known file attribute names.</returns>
    public static IEnumerable<string> GetNames(bool includeNone)
        => _nameToValueMap.Value.Keys.Where(x => includeNone || x != nameof(FileAttributes.None));

    /// <summary>
    /// Get the file attributes that mark files as not local on the system.
    /// </summary>
    public static FileAttributes NonLocalAttributes
        => OperatingSystem.IsWindows()
            ? (FileAttributes)(Win32FileAttributes.Offline | Win32FileAttributes.RecallOnOpen | Win32FileAttributes.RecallOnDataAccess)
            : FileAttributes.Offline;

    /// <summary>
    /// Parse a comma-separated list of file attribute names into a FileAttributes value.
    /// </summary>
    /// <param name="names">The comma-separated list of file attribute names.</param>
    /// <returns>The parsed FileAttributes value.</returns>
    public static FileAttributes Parse(string names)
    {
        if (string.IsNullOrWhiteSpace(names))
            return FileAttributes.None;

        var result = 0u;
        foreach (var name in names.Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_nameToValueMap.Value.TryGetValue(name, out var value))
                result |= value;
        }

        return (FileAttributes)result;
    }
}
