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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots.MacOS;

/// <summary>
/// Implements support functions around a MacOS Photos library
/// </summary>
[SupportedOSPlatform("macOS")]
internal sealed class MacOSPhotosLibrary
{
    /// <summary>
    /// Subfolder within the exported structure where photos assets are placed,
    /// to avoid collisions with other files in the library.
    /// This means that restores will place photos under this subfolder, and not inside Photos
    /// </summary>
    public const string EXPORT_SUBFOLDER = "dup_backup";

    /// <summary>
    /// Creates a new Photos library helper for the specified path
    /// </summary>
    /// <param name="path">The path to the Photos library</param>
    public MacOSPhotosLibrary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be a non-empty string", nameof(path));
    }

    /// <summary>
    /// Attempts to wrap the specified entry as a Photos library entry
    /// </summary>
    /// <param name="entry">The entry to wrap</param>
    /// <param name="macOSPhotosHandling">The Photos handling strategy</param>
    /// <param name="forcedPath">An optional forced path where the Photos library exists; if null the system default path is probed</param>
    /// <returns>The wrapped entry if it is a Photos library, or the original entry otherwise</returns>
    public static ISourceProviderEntry TryWrap(ISourceProviderEntry entry, MacOSPhotosHandling macOSPhotosHandling, string? forcedPath)
        => entry.IsFolder && !(entry is MacOSPhotosLibraryEntry) && IsPhotosLibrary(entry.Path, forcedPath)
            ? new MacOSPhotosLibraryEntry(entry, macOSPhotosHandling)
            : entry;

    /// <summary>
    /// Determines whether the specified path is a MacOS Photos library
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <param name="forcedPath">An optional forced path where the Photos library exists; if null the system default path is probed</param>
    /// <returns>True if the path is a Photos library, false otherwise</returns>
    internal static bool IsPhotosLibrary(string path, string? forcedPath)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!trimmed.EndsWith(".photoslibrary", Utility.Utility.ClientFilenameStringComparison))
            return false;

        try
        {
            if (!Directory.Exists(trimmed))
                return false;

            if (!string.IsNullOrEmpty(forcedPath))
                return string.Equals(trimmed, forcedPath, Utility.Utility.ClientFilenameStringComparison);

            // Only return true if this is the current user's system photo library
            var userPhotoLibraryPath = GetSystemPhotoLibraryPath();
            if (string.IsNullOrEmpty(userPhotoLibraryPath))
                return false;

            // Compare the resolved paths to handle symlinks and relative paths
            var resolvedPath = Path.GetFullPath(trimmed);
            var resolvedUserPath = Path.GetFullPath(userPhotoLibraryPath);

            return string.Equals(resolvedPath, resolvedUserPath, Utility.Utility.ClientFilenameStringComparison);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the path to the current user's system photo library
    /// </summary>
    /// <returns>The path to the system photo library, or null if not found</returns>
    private static string? GetSystemPhotoLibraryPath()
    {
        try
        {
            // The default system photo library is located in the Pictures folder
            // Use MyPictures special folder to handle localized folder names
            var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(picturesDirectory) || !Directory.Exists(picturesDirectory))
                return null;

            // Find .photoslibrary directories in the Pictures folder
            // The system library name can be localized (e.g., "Photos Library.photoslibrary",
            // "Fotomediathek.photoslibrary" in German, "Photothèque.photoslibrary" in French, etc.)
            var photoLibraries = Directory.GetDirectories(picturesDirectory, "*.photoslibrary", SearchOption.TopDirectoryOnly);

            // Return the first .photoslibrary found, as typically there's only one system library
            // If multiple exist, the first one is likely the system library
            return photoLibraries.Length > 0 ? photoLibraries[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the list of assets in the Photos library
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The list of assets</returns>
    public async Task<IReadOnlyList<MacOSPhotoAsset>> GetAssetsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nativeAssets = MacOSPhotosNative.ListAssets();
        var results = new List<MacOSPhotoAsset>(nativeAssets.Count);

        foreach (var native in nativeAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeFileName = CreateSafeFileName(native.Identifier, native.FileName, native.UniformTypeIdentifier, native.MediaType);
            var relativePath = Path.Combine(EXPORT_SUBFOLDER, safeFileName);

            DateTime? creation = native.CreationSeconds.HasValue ? FromUnixSeconds(native.CreationSeconds.Value) : null;
            DateTime? modification = native.ModificationSeconds.HasValue ? FromUnixSeconds(native.ModificationSeconds.Value) : null;
            long? size = native.Size >= 0 ? native.Size : null;

            results.Add(new MacOSPhotoAsset(
                native.Identifier,
                safeFileName,
                relativePath,
                native.UniformTypeIdentifier,
                native.MediaType,
                size,
                native.PixelWidth,
                native.PixelHeight,
                creation,
                modification));
        }

        return results;
    }

    /// <summary>
    /// Opens a read stream for the specified asset
    /// </summary>
    /// <param name="asset">The asset to open</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>>An open read stream for the asset</returns>
    public Task<Stream> OpenAssetStreamAsync(MacOSPhotoAsset asset, CancellationToken cancellationToken)
    {
        if (asset is null)
            throw new ArgumentNullException(nameof(asset));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MacOSPhotosNative.OpenReadStream(asset.Identifier, asset.Size));
    }

    /// <summary>
    /// Converts Unix time in seconds to a DateTime object
    /// </summary>
    /// <param name="seconds">The Unix time in seconds</param>
    /// <returns>A DateTime object representing the specified Unix time</returns>
    private static DateTime FromUnixSeconds(double seconds)
    {
        var rounded = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return DateTimeOffset.FromUnixTimeSeconds(rounded).UtcDateTime;
    }

    /// <summary>
    /// Creates a safe file name for the asset
    /// </summary>
    /// <param name="identifier">The unique identifier of the asset</param>
    /// <param name="filename">The original file name</param>
    /// <param name="uti">The Uniform Type Identifier of the asset</param>
    /// <param name="mediaType">The media type of the asset</param>
    /// <returns>A safe file name for the asset</returns>
    private static string CreateSafeFileName(string identifier, string? filename, string? uti, MacOSPhotoMediaType mediaType)
    {
        var candidate = string.IsNullOrWhiteSpace(filename) ? $"asset{GetDefaultExtension(mediaType, uti)}" : filename!;
        var sanitized = SanitizeFileName(candidate);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)))
            sanitized = sanitized + GetDefaultExtension(mediaType, uti);

        return $"{SanitizeComponent(identifier)}_{sanitized}";
    }

    /// <summary>
    /// Gets the default file extension for the specified media type and UTI
    /// </summary>
    /// <param name="mediaType">The media type of the asset</param>
    /// <param name="uti">The Uniform Type Identifier of the asset</param>
    /// <returns>The default file extension for the specified media type and UTI</returns>
    private static string GetDefaultExtension(MacOSPhotoMediaType mediaType, string? uti)
    {
        if (!string.IsNullOrEmpty(uti))
        {
            var lower = uti.ToLowerInvariant();
            if (lower.Contains("heic") || lower.Contains("heif"))
                return ".heic";
            if (lower.Contains("png"))
                return ".png";
            if (lower.Contains("gif"))
                return ".gif";
            if (lower.Contains("jpeg") || lower.Contains("jpg"))
                return ".jpg";
            if (lower.Contains("tiff"))
                return ".tif";
            if (lower.Contains("mov"))
                return ".mov";
            if (lower.Contains("mp4") || lower.Contains("m4v"))
                return ".mp4";
            if (lower.Contains("m4a") || lower.Contains("aac"))
                return ".m4a";
        }

        return mediaType switch
        {
            MacOSPhotoMediaType.Image => ".jpg",
            MacOSPhotoMediaType.Video => ".mov",
            MacOSPhotoMediaType.Audio => ".m4a",
            _ => ".bin"
        };
    }

    /// <summary>
    /// Sanitizes a file name by replacing invalid characters with underscores
    /// </summary>
    /// <param name="name">The file name to sanitize</param>
    /// <returns>>The sanitized file name</returns>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (invalid.Contains(ch) || ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)
                builder.Append('_');
            else
                builder.Append(ch);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "asset" : result;
    }

    /// <summary>
    /// Sanitizes a path component by replacing invalid characters with underscores
    /// </summary>
    /// <param name="value">The path component to sanitize</param>
    /// <returns>The sanitized path component</returns>
    private static string SanitizeComponent(string value)
    {
        var invalid = Path.GetInvalidPathChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (invalid.Contains(ch) || ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar)
                builder.Append('_');
            else
                builder.Append(ch);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "asset" : result;
    }

}

/// <summary>
/// Represents a photo asset within a MacOS Photos library
/// </summary>
/// <param name="Identifier">The unique identifier of the asset</param>
/// <param name="FileName">The file name of the asset</param>
/// <param name="RelativePath">The relative path of the asset within the export structure</param>
/// <param name="UniformTypeIdentifier">The Uniform Type Identifier of the asset</param>
/// <param name="MediaType">The media type of the asset</param>
/// <param name="Size">The size of the asset in bytes</param>
/// <param name="PixelWidth">The width of the asset in pixels</param>
/// <param name="PixelHeight">>The height of the asset in pixels</param>
/// <param name="CreatedUtc">The creation time of the asset in UTC</param>
/// <param name="ModifiedUtc">>The modification time of the asset in UTC</param>
[SupportedOSPlatform("macOS")]
public sealed record MacOSPhotoAsset(
    string Identifier,
    string FileName,
    string RelativePath,
    string? UniformTypeIdentifier,
    MacOSPhotoMediaType MediaType,
    long? Size,
    int PixelWidth,
    int PixelHeight,
    DateTime? CreatedUtc,
    DateTime? ModifiedUtc
);

/// <summary>
/// Defines media types for MacOS Photos assets
/// </summary>
public enum MacOSPhotoMediaType
{
    /// <summary>
    /// Unknown media type
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// Image media type
    /// </summary>
    Image = 1,
    /// <summary>
    /// Video media type
    /// </summary>
    Video = 2,
    /// <summary>
    /// Audio media type
    /// </summary>
    Audio = 3
}

/// <summary>
/// Represents errors that occur during MacOS Photos library operations
/// </summary>
public sealed class MacOSPhotosException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MacOSPhotosException"/> class
    /// </summary>
    /// <param name="message">The error message</param>
    public MacOSPhotosException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MacOSPhotosException"/> class
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public MacOSPhotosException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
