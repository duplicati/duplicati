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
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vanara.PInvoke;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Read-only, non-seekable stream that returns only the primary
/// data stream of <paramref name="path"/> by using BackupRead.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BackupDataStream : Stream
{
    /// <summary>
    /// The file handle to the opened file with FILE_FLAG_BACKUP_SEMANTICS.
    /// </summary>
    private readonly Kernel32.SafeHFILE _file;
    /// <summary>
    /// The internal buffer used to read data from the file.
    /// </summary>
    private readonly byte[] _buffer;
    /// <summary>
    /// The size of the internal buffer in bytes.
    /// </summary>
    private readonly uint _bufferSize;
    /// <summary>
    /// The handle to the pinned buffer, used to pass it to BackupRead.
    /// </summary>
    private readonly GCHandle _bufHandle;
    /// <summary>
    /// Pointer to the pinned buffer, used to pass it to BackupRead.
    /// </summary>
    private readonly IntPtr _bufPtr;

    /// <summary>
    /// The current position in the internal buffer.
    /// </summary>
    private int _bufPos, _bufLen;
    /// <summary>
    /// The context for BackupRead, used to maintain state between calls.
    /// </summary>
    private IntPtr _context;
    /// <summary>
    /// Indicates whether the current stream is inside the primary data stream.
    /// The first chunk of every stream starts with a WIN32_STREAM_ID header,
    /// </summary>
    private bool _insideData;
    /// <summary>
    /// The total number of bytes read from the file so far.
    /// </summary>
    private long _bytesReadTotal;
    /// <summary>
    /// The initial data length of the file
    /// </summary>
    private readonly long _length;
    /// <summary>
    /// The size of the data in the stream
    /// </summary>
    private long  _dataSize;
    /// <summary>
    /// Flag indicating if we know the stream size
    /// </summary>
    private bool _dataSizeKnown;

    /// <summary>
    /// Creates a new <see cref="BackupDataStream"/> for the specified file path.
    /// Note that the call context must have the SeBackupPrivilege enabled.
    /// </summary>
    /// <param name="path">The path to the file to read</param>
    public BackupDataStream(string path)
        : this(path, 64 * 1024)
    {
    }

    /// <summary>
    /// Creates a new <see cref="BackupDataStream"/> for the specified file path.
    /// Note that the call context must have the SeBackupPrivilege enabled.
    /// </summary>
    /// <param name="path">The path to the file to read</param>
    /// <param name="bufferSize">The buffer size, if not using the default</param>
    public BackupDataStream(string path, int bufferSize)
    {
        if (bufferSize < 4 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        _bufferSize = (uint)bufferSize;
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        // Open the file with FILE_FLAG_BACKUP_SEMANTICS
        _file = Kernel32.CreateFile(
            lpFileName: path,
            dwDesiredAccess: Kernel32.FileAccess.FILE_READ_DATA,
            dwShareMode: FileShare.ReadWrite | FileShare.Delete,
            lpSecurityAttributes: null,
            dwCreationDisposition: FileMode.Open,
            dwFlagsAndAttributes: FileFlagsAndAttributes.FILE_FLAG_BACKUP_SEMANTICS,
            hTemplateFile: default);

        if (_file.IsInvalid)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        if (!Kernel32.GetFileSizeEx(_file, out _length))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        // Pin the buffer once
        _bufHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufPtr = _bufHandle.AddrOfPinnedObject();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (_context != IntPtr.Zero)
                Kernel32.BackupRead(_file, IntPtr.Zero, 0, out _, true, false, ref _context);

            _file.Dispose();
        }
        finally
        {
            if (_bufHandle.IsAllocated) _bufHandle.Free();
            ArrayPool<byte>.Shared.Return(_buffer);
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _dataSizeKnown ? _dataSize : _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _bytesReadTotal;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] dest, int offset, int count)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        if ((uint)offset > dest.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if ((uint)count > dest.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;

        int copied = 0;
        while (count > 0)
        {
            // Buffer empty? – Refill from BackupRead
            if (_bufPos == _bufLen && !FillBuffer())
                break;                         // EOF

            int take = Math.Min(_bufLen - _bufPos, count);
            Buffer.BlockCopy(_buffer, _bufPos, dest, offset, take);

            _bufPos += take;
            offset += take;
            count -= take;
            copied += take;
            _bytesReadTotal += take;
        }

        return copied;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Flush() { /* no-op */ }

    /// <summary>
    /// Fills the internal buffer with data from the file using BackupRead.
    /// </summary>
    /// <returns><c>true</c> if the buffer was filled with data, <c>false</c> if there are no more data to read.</returns>
    private bool FillBuffer()
    {
        _bufPos = 0;
        _bufLen = 0;

        while (true)
        {
            if (!Kernel32.BackupRead(_file, _bufPtr, _bufferSize, out var read, false, false, ref _context))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            if (read == 0)
                return false;

            // First chunk of every stream starts with a WIN32_STREAM_ID header
            if (!_insideData)
            {
                var hdr = Marshal.PtrToStructure<Kernel32.WIN32_STREAM_ID>(_bufPtr);

                // Skip streams that aren't BACKUP_DATA (primary data stream)
                if (hdr.dwStreamId != Kernel32.BACKUP_STREAM_ID.BACKUP_DATA)
                {
                    var sz = (ulong)hdr.Size;
                    Kernel32.BackupSeek(_file, (uint)sz, (uint)(sz >> 32), out _, out _, ref _context);
                    continue; // look at next stream
                }

                _insideData = true;
                _dataSize = hdr.Size;
                _dataSizeKnown = true;

                // Remove the header + stream name bytes from current buffer
                int headerBytes = 20 + (int)hdr.dwStreamNameSize;

                _bufLen = (int)read - headerBytes;
                if (_bufLen > 0)
                    Buffer.BlockCopy(_buffer, headerBytes, _buffer, 0, _bufLen);
            }
            else
            {
                // subsequent chunks are raw data
                _bufLen = (int)read;
            }

            // may be zero for sparse holes
            return _bufLen > 0;
        }
    }
}