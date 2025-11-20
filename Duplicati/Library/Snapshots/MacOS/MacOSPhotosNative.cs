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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Snapshots.MacOS;

/// <summary>
/// Wrapper for native MacOS Photos library access
/// </summary>
[SupportedOSPlatform("macOS")]
internal static class MacOSPhotosNative
{
    /// <summary>
    /// The name of the native library
    /// </summary>
    private const string LibraryName = "DuplicatiPhotos";

    /// <summary>
    /// Lists all assets in the Photos library
    /// </summary>
    /// <returns>The list of assets</returns>
    public static IReadOnlyList<NativeAsset> ListAssets()
    {
        var result = NativeMethods.DuplicatiPhotosEnumerateAssets(out var assetsPtr, out var count, out var errorPtr);
        try
        {
            if (result != 0)
                throw new MacOSPhotosException(ConsumeErrorMessage(ref errorPtr) ?? "Failed to enumerate Photos assets.");

            if (assetsPtr == IntPtr.Zero || count == UIntPtr.Zero)
                return Array.Empty<NativeAsset>();

            var assetCount = checked((int)count.ToUInt64());
            var structSize = Marshal.SizeOf<NativeAssetNative>();
            var assets = new List<NativeAsset>(assetCount);

            for (var index = 0; index < assetCount; index++)
            {
                var entryPtr = assetsPtr + (index * structSize);
                var native = Marshal.PtrToStructure<NativeAssetNative>(entryPtr);

                var identifier = Marshal.PtrToStringUTF8(native.Identifier) ?? string.Empty;
                var fileName = Marshal.PtrToStringUTF8(native.FileName) ?? string.Empty;
                var uti = Marshal.PtrToStringUTF8(native.UniformTypeIdentifier);

                assets.Add(new NativeAsset(
                    identifier,
                    fileName,
                    uti,
                    (MacOSPhotoMediaType)native.MediaType,
                    native.Size,
                    native.PixelWidth,
                    native.PixelHeight,
                    NormalizeOptional(native.CreationSeconds),
                    NormalizeOptional(native.ModificationSeconds)));
            }

            return assets;
        }
        finally
        {
            if (assetsPtr != IntPtr.Zero)
                NativeMethods.DuplicatiPhotosFreeAssets(assetsPtr, count);

            if (errorPtr != IntPtr.Zero)
                NativeMethods.DuplicatiPhotosFreeString(errorPtr);
        }
    }

    /// <summary>
    /// Opens a read stream for the specified asset identifier
    /// </summary>
    /// <param name="identifier">The unique identifier of the asset</param>
    /// <param name="size">The size of the asset, or null to determine size automatically</param>
    /// <returns>The read stream for the asset</returns>
    public static Stream OpenReadStream(string identifier, long? size = null)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier is required", nameof(identifier));

        var result = NativeMethods.DuplicatiPhotosOpenAsset(identifier, out var handle, out var errorPtr);
        if (result != 0 || handle.IsInvalid)
        {
            var message = ConsumeErrorMessage(ref errorPtr) ?? "Failed to open Photos asset.";
            handle.Dispose();
            throw new MacOSPhotosException(message);
        }

        // If size is not provided, try to get it from the handle
        var assetSize = size ?? GetAssetSize(handle);

        return new MacOSPhotosNativeStream(handle, assetSize);
    }

    /// <summary>
    /// Gets the size of the asset from the handle
    /// </summary>
    /// <param name="handle">The asset handle</param>
    /// <returns>The size of the asset</returns>
    internal static long GetAssetSize(SafeAssetHandle handle)
    {
        if (handle.IsInvalid)
            throw new ObjectDisposedException(nameof(SafeAssetHandle));

        var result = NativeMethods.DuplicatiPhotosGetAssetSize(handle, out var size, out var errorPtr);
        if (result != 0)
        {
            var message = ConsumeErrorMessage(ref errorPtr) ?? "Failed to get Photos asset size.";
            throw new MacOSPhotosException(message);
        }

        if (errorPtr != IntPtr.Zero)
        {
            NativeMethods.DuplicatiPhotosFreeString(errorPtr);
        }

        return size;
    }

    /// <summary>
    /// Reads data from the asset into the provided buffer
    /// </summary>
    /// <param name="handle">The asset handle</param>
    /// <param name="buffer">The buffer to read data into</param>
    /// <returns>The number of bytes read</returns>
    internal static int ReadAsset(SafeAssetHandle handle, Span<byte> buffer)
    {
        if (handle.IsInvalid)
            throw new ObjectDisposedException(nameof(SafeAssetHandle));

        if (buffer.Length == 0)
            return 0;

        var errorPtr = IntPtr.Zero;
        int bytesRead;

        // TODO: Optimize to read directly into the provided buffer
        var tempbuffer = new byte[buffer.Length];
        var result = NativeMethods.DuplicatiPhotosReadAsset(handle, tempbuffer, (nuint)tempbuffer.Length, out errorPtr);
        if (result < 0)
        {
            var message = ConsumeErrorMessage(ref errorPtr) ?? "Failed to read Photos asset data.";
            throw new MacOSPhotosException(message);
        }
        tempbuffer.AsSpan(0, (int)result).CopyTo(buffer);
        bytesRead = checked((int)result);

        if (errorPtr != IntPtr.Zero)
        {
            NativeMethods.DuplicatiPhotosFreeString(errorPtr);
            errorPtr = IntPtr.Zero;
        }

        return bytesRead;
    }

    /// <summary>
    /// Normalizes an optional double value, returning null if it is NaN
    /// </summary>
    /// <param name="value">The double value</param>
    /// <returns>>The normalized value or null</returns>
    private static double? NormalizeOptional(double value)
        => double.IsNaN(value) ? null : value;

    /// <summary>
    /// Consumes an error message pointer and frees the native memory
    /// </summary>
    /// <param name="pointer">The error message pointer</param>
    /// <returns>The error message string or null</returns>
    private static string? ConsumeErrorMessage(ref IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            NativeMethods.DuplicatiPhotosFreeString(pointer);
            pointer = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Represents a native asset in the Photos library
    /// </summary>
    /// <param name="Identifier">The unique identifier of the asset</param>
    /// <param name="FileName">The original file name</param>
    /// <param name="UniformTypeIdentifier">The Uniform Type Identifier of the asset</param>
    /// <param name="MediaType">The media type of the asset</param>
    /// <param name="Size">The size of the asset in bytes</param>
    /// <param name="PixelWidth">The pixel width of the asset</param>
    /// <param name="PixelHeight">>The pixel height of the asset</param>
    /// <param name="CreationSeconds">The creation time in seconds since Unix epoch</param>
    /// <param name="ModificationSeconds">>The modification time in seconds since Unix epoch</param>
    public sealed record NativeAsset(
        string Identifier,
        string FileName,
        string? UniformTypeIdentifier,
        MacOSPhotoMediaType MediaType,
        long Size,
        int PixelWidth,
        int PixelHeight,
        double? CreationSeconds,
        double? ModificationSeconds);

    /// <summary>
    /// Native representation of an asset
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAssetNative
    {
        /// <summary>
        /// The unique identifier of the asset
        /// </summary>
        internal IntPtr Identifier;
        /// <summary>
        /// The original file name
        /// </summary>
        internal IntPtr FileName;
        /// <summary>
        /// The Uniform Type Identifier of the asset
        /// </summary>
        internal IntPtr UniformTypeIdentifier;
        /// <summary>
        /// The size of the asset in bytes
        /// </summary>
        internal long Size;
        /// <summary>
        /// The media type of the asset
        /// </summary>
        internal int MediaType;
        /// <summary>
        /// The pixel width of the asset
        /// </summary>
        internal int PixelWidth;
        /// <summary>
        /// The pixel height of the asset
        /// </summary>
        internal int PixelHeight;
        /// <summary>
        /// The creation time in seconds since Unix epoch
        /// </summary>
        internal double CreationSeconds;
        /// <summary>
        /// The modification time in seconds since Unix epoch
        /// </summary>
        internal double ModificationSeconds;
    }

    /// <summary>
    /// Safe handle for a Photos asset
    /// </summary>
    internal sealed class SafeAssetHandle : SafeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SafeAssetHandle"/> class
        /// </summary>
        private SafeAssetHandle()
            : base(IntPtr.Zero, true)
        {
        }

        /// <summary>
        /// Indicates if the handle is invalid
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>
        /// Releases the handle
        /// </summary>
        /// <returns>True if the handle was released successfully; otherwise, false</returns>
        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
                NativeMethods.DuplicatiPhotosCloseAsset(handle);

            return true;
        }
    }

    /// <summary>
    /// Native method imports
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// Enumerates the assets in the Photos library
        /// </summary>
        /// <param name="assets">The pointer to the array of assets</param>
        /// <param name="count">The number of assets</param>
        /// <param name="errorMessage">The error message, if any</param>
        /// <returns>The result code</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DuplicatiPhotosEnumerateAssets(out IntPtr assets, out UIntPtr count, out IntPtr errorMessage);

        /// <summary>
        /// Frees the assets array
        /// </summary>
        /// <param name="assets">The pointer to the array of assets</param>
        /// <param name="count">The number of assets</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DuplicatiPhotosFreeAssets(IntPtr assets, UIntPtr count);

        /// <summary>
        /// Frees a string allocated by the native library
        /// </summary>
        /// <param name="value">The string pointer</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DuplicatiPhotosFreeString(IntPtr value);

        /// <summary>
        /// Opens an asset for reading
        /// </summary>
        /// <param name="identifier">The unique identifier of the asset</param>
        /// <param name="handle">The output asset handle</param>
        /// <param name="errorMessage">The error message, if any</param>
        /// <returns>The result code</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DuplicatiPhotosOpenAsset([MarshalAs(UnmanagedType.LPUTF8Str)] string identifier, out SafeAssetHandle handle, out IntPtr errorMessage);

        /// <summary>
        /// Reads data from the asset into the provided buffer
        /// </summary>
        /// <param name="handle">The asset handle</param>
        /// <param name="buffer">The buffer to read data into</param>
        /// <param name="length">The length of the buffer</param>
        /// <param name="errorMessage"> The error message, if any</param>
        /// <returns>The number of bytes read</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint DuplicatiPhotosReadAsset(SafeAssetHandle handle, byte[] buffer, nuint length, out IntPtr errorMessage);

        /// <summary>
        /// Closes the asset handle
        /// </summary>
        /// <param name="handle">The asset handle</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DuplicatiPhotosCloseAsset(IntPtr handle);

        /// <summary>
        /// Gets the size of the asset
        /// </summary>
        /// <param name="handle">The asset handle</param>
        /// <param name="size">The size of the asset</param>
        /// <param name="errorMessage">The error message, if any</param>
        /// <returns>The result code</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DuplicatiPhotosGetAssetSize(SafeAssetHandle handle, out long size, out IntPtr errorMessage);
    }
}
