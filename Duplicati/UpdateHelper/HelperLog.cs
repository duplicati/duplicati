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

namespace Duplicati.UpdateHelper;

/// <summary>
/// Appends timestamped lines to a log file and to stderr
/// </summary>
internal sealed class HelperLog : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;

    /// <summary>
    /// The path of the current log file
    /// </summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Starts, or redirects, logging to the given file
    /// </summary>
    /// <param name="path">The file to append to</param>
    public void Redirect(string path)
    {
        lock (_lock)
        {
            if (string.Equals(Path, path, StringComparison.Ordinal))
                return;

            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                _writer?.Dispose();
                _writer = writer;
                Path = path;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to open log file {path}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Writes a line to the log
    /// </summary>
    /// <param name="message">The message to write</param>
    public void Write(string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (_lock)
        {
            try { _writer?.WriteLine(line); } catch { }
            try { Console.Error.WriteLine(line); } catch { }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
