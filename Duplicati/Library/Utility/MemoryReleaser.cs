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
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Utility;

/// <summary>
/// Gives memory that a finished operation no longer uses back to the operating system.
/// </summary>
/// <remarks>
/// A long-running process, such as the server, keeps the memory of a finished operation:
/// the garbage collector does not run while the process is idle, and on Linux the C library
/// keeps the native memory it has freed instead of returning it.
/// </remarks>
public static class MemoryReleaser
{
    /// <summary>
    /// The log tag
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(MemoryReleaser));

    /// <summary>
    /// Set when malloc_trim is missing, such as with the musl C library
    /// </summary>
    private static bool _trimUnavailable;

    /// <summary>
    /// Collects the garbage, compacting the large object heap, and on Linux returns the freed
    /// native memory to the operating system.
    /// </summary>
    public static void ReleaseUnusedMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

            if (OperatingSystem.IsLinux() && !_trimUnavailable)
                TrimNativeHeap();
        }
        catch (Exception ex)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "MemoryReleaseFailed", ex, "Failed to release unused memory");
        }
    }

    /// <summary>
    /// Returns the freed native memory to the operating system.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static void TrimNativeHeap()
    {
        try
        {
            malloc_trim(UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _trimUnavailable = true;
        }
    }

    /// <summary>
    /// Releases the free memory at the top of the glibc heaps and in their unused pages.
    /// </summary>
    /// <param name="pad">The free space to leave at the top of the heap</param>
    /// <returns>1 if memory was returned to the operating system, otherwise 0</returns>
    [SupportedOSPlatform("linux")]
    [DllImport("libc")]
    private static extern int malloc_trim(UIntPtr pad);
}
