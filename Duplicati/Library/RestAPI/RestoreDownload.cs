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

#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Duplicati.Library.Utility;

namespace Duplicati.Server;

/// <summary>
/// A restore to the browser: the files are restored to a temporary folder,
/// packed into one file, and the browser downloads that file
/// </summary>
public static class RestoreDownload
{
    /// <summary>
    /// The origin of a registered download, followed by the name to download it as
    /// </summary>
    public const string TempFileOriginPrefix = "restore-download:";

    /// <summary>
    /// The action of the notification that tells a download is ready, followed by the temp file id
    /// </summary>
    public const string NotificationActionPrefix = "restore:download:";

    /// <summary>
    /// The single operation token that allows the download
    /// </summary>
    public const string TokenOperation = "restore-download";

    /// <summary>
    /// The name of the download when several files are restored
    /// </summary>
    public const string ZipName = "duplicati-restore.zip";

    /// <summary>
    /// How long a download is kept
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>
    /// Packs the restored files into one file to download: a single file as it is, several in a zip file.
    /// </summary>
    /// <param name="folder">The folder the files were restored to</param>
    /// <returns>The file to download and the name to download it as, or null if no file was restored</returns>
    public static (string Path, string Name)? Pack(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Take(2).ToList();
        if (files.Count == 0)
            return null;

        using var target = new TempFile();
        if (files.Count == 1)
        {
            File.Move(files[0], target, true);
            target.Protected = true;
            return (target, Path.GetFileName(files[0]));
        }

        File.Delete(target);
        ZipFile.CreateFromDirectory(folder, target, CompressionLevel.Optimal, false);
        target.Protected = true;
        return (target, ZipName);
    }

    /// <summary>
    /// Gets the name to download a registered file as
    /// </summary>
    /// <param name="origin">The origin the file was registered with</param>
    /// <returns>The name, or null if the file is not a restore download</returns>
    public static string? GetDownloadName(string? origin)
        => origin != null && origin.StartsWith(TempFileOriginPrefix, StringComparison.Ordinal) && origin.Length > TempFileOriginPrefix.Length
            ? origin.Substring(TempFileOriginPrefix.Length)
            : null;
}
