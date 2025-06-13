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

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// Representation of the current active transfer.
    /// </summary>
    /// <param name="BackendAction">The action being performed by the backend</param>
    /// <param name="BackendPath">The path of the file being transferred</param>
    /// <param name="BackendFileSize">The total size of the file being transferred</param>
    /// <param name="BackendFileProgress">The current progress of the file transfer</param>
    /// <param name="BackendSpeed">The current speed of the file transfer</param>
    /// <param name="BackendIsBlocking">Indicates if the backend operation is blocking</param>
    public sealed record ActiveTransfer(
        string BackendAction,
        string BackendPath,
        long BackendFileSize,
        long BackendFileProgress,
        long BackendSpeed,
        bool BackendIsBlocking
    );

    public interface IProgressEventData
    {
        string? BackupID { get; }
        long TaskID { get; }

        string BackendAction { get; }
        string? BackendPath { get; }
        long BackendFileSize { get; }
        long BackendFileProgress { get; }
        long BackendSpeed { get; }
        bool BackendIsBlocking { get; }

        string? CurrentFilename { get; }
        long CurrentFilesize { get; }
        long CurrentFileoffset { get; }
        bool CurrentFilecomplete { get; }

        string Phase { get; }
        float OverallProgress { get; }
        long ProcessedFileCount { get; }
        long ProcessedFileSize { get; }
        long TotalFileCount { get; }
        long TotalFileSize { get; }
        bool StillCounting { get; }

        ActiveTransfer[]? ActiveTransfers { get; }
    }
}
