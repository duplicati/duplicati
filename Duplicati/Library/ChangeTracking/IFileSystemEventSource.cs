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
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// A source of file system events below one root. The abstraction keeps the
/// operating system's watcher out of the tests.
/// </summary>
public interface IFileSystemEventSource : IDisposable
{
    /// <summary>
    /// Raised with the full path and what was observed for it. Handlers must return quickly.
    /// </summary>
    event Action<string, ChangeKind>? Changed;

    /// <summary>
    /// Raised when events may have been lost, so the recording can no longer be trusted
    /// </summary>
    event Action<Exception>? Error;

    /// <summary>
    /// Starts delivering events. Throws if the root cannot be watched.
    /// </summary>
    void Start();
}

/// <summary>
/// Raised through <see cref="IFileSystemEventSource.Error"/> when an event could not be
/// recorded as a usable path. The watcher keeps working, but the recording is incomplete.
/// </summary>
public sealed class LostEventsException : Exception
{
    /// <summary>
    /// Creates a new exception
    /// </summary>
    /// <param name="message">The message</param>
    public LostEventsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Creates an event source for a root path
/// </summary>
/// <param name="root">The root to watch; a folder or a single file</param>
/// <returns>The event source, not yet started</returns>
public delegate IFileSystemEventSource FileSystemEventSourceFactory(string root);

/// <summary>
/// An event source backed by the operating system's file system watcher
/// </summary>
public sealed class FileSystemWatcherEventSource : IFileSystemEventSource
{
    /// <summary>
    /// The largest buffer the Windows implementation accepts
    /// </summary>
    private const int WINDOWS_MAX_BUFFER_SIZE = 64 * 1024;

    private readonly string _root;
    private System.IO.FileSystemWatcher? _watcher;
    /// <summary>
    /// Whether the root is a single file, watched through its folder
    /// </summary>
    private bool _isFileRoot;

    /// <inheritdoc />
    public event Action<string, ChangeKind>? Changed;

    /// <inheritdoc />
    public event Action<Exception>? Error;

    /// <summary>
    /// Creates a new event source
    /// </summary>
    /// <param name="root">The root to watch</param>
    public FileSystemWatcherEventSource(string root)
    {
        _root = root;
    }

    /// <summary>
    /// The factory for this event source
    /// </summary>
    /// <param name="root">The root to watch</param>
    /// <returns>The event source</returns>
    public static IFileSystemEventSource Create(string root)
        => new FileSystemWatcherEventSource(root);

    /// <inheritdoc />
    public void Start()
    {
        if (_watcher != null)
            throw new InvalidOperationException("The event source is already started");

        string folder;
        string filter;
        bool includeSubdirectories;

        if (Directory.Exists(_root))
        {
            folder = _root;
            filter = "*";
            includeSubdirectories = true;
        }
        else if (File.Exists(_root))
        {
            // A single file is watched through its folder
            folder = Path.GetDirectoryName(_root) ?? throw new DirectoryNotFoundException($"The file has no folder: {_root}");
            filter = Path.GetFileName(_root);
            includeSubdirectories = false;
            _isFileRoot = true;
        }
        else
        {
            throw new DirectoryNotFoundException($"The source path does not exist: {_root}");
        }

        var watcher = new FileSystemWatcher(folder, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            // Access time changes are noise; everything else can change what a backup stores
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.Security
        };

        if (OperatingSystem.IsWindows())
            watcher.InternalBufferSize = WINDOWS_MAX_BUFFER_SIZE;

        watcher.Created += (_, e) => Raise(e.FullPath, ChangeKind.Created | DirectoryHint(e.FullPath));
        watcher.Changed += (_, e) => Raise(e.FullPath, ChangeKind.Changed | DirectoryHint(e.FullPath));
        // The entry is gone, so its type is unknown
        watcher.Deleted += (_, e) => Raise(e.FullPath, ChangeKind.Deleted);
        watcher.Renamed += (_, e) =>
        {
            Raise(e.OldFullPath, ChangeKind.RenamedFrom);
            Raise(e.FullPath, ChangeKind.RenamedTo | DirectoryHint(e.FullPath));
        };
        watcher.Error += (_, e) => Error?.Invoke(e.GetException());

        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void Raise(string path, ChangeKind kind)
    {
        try
        {
            // A rename onto a single-file root also reports the other name, such as the
            // temporary file of an atomic save, which is not part of the source
            if (_isFileRoot && !string.Equals(path, _root, Utility.Utility.ClientFilenameStringComparison))
                return;

            // Windows may report the short 8.3 name, which the backup does not store
            if (OperatingSystem.IsWindows() && WindowsShortNames.LooksShort(path))
            {
                var expanded = WindowsShortNames.TryExpand(path);
                if (expanded == null)
                {
                    Error?.Invoke(new LostEventsException($"The short name {path} could not be expanded to the stored name"));
                    return;
                }
                path = expanded;
            }

            Changed?.Invoke(path, kind);
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }

    private static ChangeKind DirectoryHint(string path)
    {
        try
        {
            return Directory.Exists(path) ? ChangeKind.IsDirectoryHint : ChangeKind.None;
        }
        catch
        {
            return ChangeKind.None;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher == null)
            return;

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch
        {
            // The watcher is being discarded
        }

        watcher.Dispose();
    }
}

/// <summary>
/// Expands Windows short 8.3 names to long names
/// </summary>
internal static class WindowsShortNames
{
    /// <summary>
    /// A path segment that looks like a generated short name, such as LONGFI~1.TXT
    /// </summary>
    private static readonly Regex SHORT_SEGMENT = new(@"(^|[\\/])[^\\/~]{1,6}~[0-9]+(\.[^\\/.]{0,3})?($|[\\/])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
    private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

    /// <summary>
    /// Whether a path has a segment that may be a short name
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns>True if the path may contain a short name</returns>
    public static bool LooksShort(string path)
        => SHORT_SEGMENT.IsMatch(path);

    /// <summary>
    /// Expands the short names in an existing path
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns>The long path, or null if it cannot be expanded, as when the path no longer exists</returns>
    [SupportedOSPlatform("windows")]
    public static string? TryExpand(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
        if (length > buffer.Capacity)
        {
            buffer = new StringBuilder((int)length);
            length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
        }

        return length == 0 || length > buffer.Capacity ? null : buffer.ToString();
    }
}
