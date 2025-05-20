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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// Extension methods for exceptions
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Flattens an exception and its inner exceptions
    /// </summary>
    /// <param name="ex">The exception to flatten</param>
    /// <returns>An enumerable of exceptions</returns>
    public static IEnumerable<Exception> FlattenException(Exception? ex)
    {
        if (ex == null)
            yield break;

        yield return ex;

        if (ex is AggregateException aex)
            foreach (var iex in aex.Flatten().InnerExceptions)
                foreach (var iex2 in FlattenException(iex))
                    yield return iex2;

        foreach (var iex in FlattenException(ex.InnerException))
            yield return iex;
    }

    /// <summary>
    /// Checks if an exception is a stop, cancel or timeout exception
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a stop or cancel exception, <c>false</c> otherwise</returns>
    public static bool IsAbortOrCancelException(this Exception ex)
        => ex is OperationCanceledException || ex is ThreadAbortException || ex is TaskCanceledException || ex is TimeoutException;

    /// <summary>
    /// Checks if an exception is a stop exception
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a stop exception, <c>false</c> otherwise</returns>
    public static bool IsAbortException(this Exception ex)
        => ex is OperationCanceledException || ex is ThreadAbortException;

    /// <summary>
    /// List of Windows error codes that indicate a permission denied error
    /// </summary>
    private static readonly int[] WindowsPermissionDeniedCodes = {
            5,    // ERROR_ACCESS_DENIED
            19,   // ERROR_WRITE_PROTECT
            65,   // ERROR_NETWORK_ACCESS_DENIED
            82,   // ERROR_CANNOT_MAKE
            1314, // ERROR_PRIVILEGE_NOT_HELD
        };

    /// <summary>
    /// List of Windows error codes that indicate a file lock error
    /// </summary>
    private static readonly int[] WindowsFileLockCodes = {
            32, // ERROR_SHARING_VIOLATION
            33  // ERROR_LOCK_VIOLATION
        };

    /// <summary>
    /// List of Windows error codes that indicate a path not found error
    /// </summary>
    private static readonly int[] WindowsPathNotFoundCodes = {
            2,    // ERROR_FILE_NOT_FOUND
            3,    // ERROR_PATH_NOT_FOUND
            21,   // ERROR_NOT_READY (e.g., missing drive)
            267   // ERROR_DIRECTORY
        };

    /// <summary>
    /// List of Windows error codes that indicate a path too long error
    /// </summary>
    private static readonly int[] WindowsPathTooLongCodes = {
            206,  // ERROR_FILENAME_EXCED_RANGE
            3     // ERROR_PATH_NOT_FOUND (used when long path causes resolution failure)
        };

    /// <summary>
    /// List of Posix error codes that indicate a permission denied error
    /// </summary>
    private static readonly int[] PosixPermissionErrnos = {
            1,   // EPERM
            13,  // EACCES
            30,  // EROFS - Read-only file system
        };

    /// <summary>
    /// List of Posix error codes that indicate a file lock error
    /// </summary>
    private static readonly int[] PosixFileLockErrnos = {
            // Linux file lock errors are not always distinguishable
            11  // EAGAIN (on NFS), or temporary unavailable (used for lock contention in some cases)
        };

    /// <summary>
    /// List of Posix error codes that indicate a path not found error
    /// </summary>
    private static readonly int[] PosixPathNotFoundErrnos = {
            2,    // ENOENT - No such file or directory
            20    // ENOTDIR - A path component is not a directory
        };

    /// <summary>
    /// List of Posix error codes that indicate a path too long error
    /// </summary>
    private static readonly int[] PosixPathTooLongErrnos = {
            36,   // ENAMETOOLONG - File name too long
            63    // ENAMETOOLONG (on some macOS versions)
        };


    /// <summary>
    /// Checks if an exception is a permission denied error
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a permission denied error, <c>false</c> otherwise</returns>
    public static bool IsPermissionDeniedException(this Exception? ex)
    {
        if (ex is UnauthorizedAccessException)
            return true;

        if (ex is Win32Exception win32Ex && OperatingSystem.IsWindows())
            return WindowsPermissionDeniedCodes.Contains(win32Ex.NativeErrorCode);

        if (ex is IOException ioEx)
        {
            if (ioEx.HResult == unchecked((int)0x80070005)) // E_ACCESSDENIED
                return true;

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (ioEx.InnerException is Win32Exception innerWin32 &&
                    PosixPermissionErrnos.Contains(innerWin32.NativeErrorCode))
                    return true;

                var errno = ioEx.HResult & 0xFFFF;
                return PosixPermissionErrnos.Contains(errno);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an exception is a file lock error
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a file lock error, <c>false</c> otherwise</returns>
    public static bool IsFileLockedException(this Exception? ex)
    {
        if (ex is IOException ioEx)
        {
            if (OperatingSystem.IsWindows())
            {
                // Check inner Win32Exception
                if (ioEx.InnerException is Win32Exception innerWin32 &&
                    WindowsFileLockCodes.Contains(innerWin32.NativeErrorCode))
                    return true;

                // Fallback to HRESULT
                var winCode = ioEx.HResult & 0xFFFF;
                return WindowsFileLockCodes.Contains(winCode);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (ioEx.InnerException is Win32Exception innerWin32 &&
                    PosixFileLockErrnos.Contains(innerWin32.NativeErrorCode))
                    return true;

                var errno = ioEx.HResult & 0xFFFF;
                return PosixFileLockErrnos.Contains(errno);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an exception is a path not found error
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a path not found error, <c>false</c> otherwise</returns>
    public static bool IsPathNotFoundException(this Exception? ex)
    {
        if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            return true;

        if (ex is Win32Exception win32Ex && OperatingSystem.IsWindows())
            return WindowsPathNotFoundCodes.Contains(win32Ex.NativeErrorCode);

        if (ex is IOException ioEx)
        {
            var code = ioEx.HResult & 0xFFFF;

            if (OperatingSystem.IsWindows())
                return WindowsPathNotFoundCodes.Contains(code);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (ioEx.InnerException is Win32Exception innerWin32 &&
                    PosixPathNotFoundErrnos.Contains(innerWin32.NativeErrorCode))
                    return true;

                return PosixPathNotFoundErrnos.Contains(code);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an exception is a path too long error
    /// </summary>
    /// <param name="ex">The operation to check</param>
    /// <returns><c>true</c> if the exception is a path too long error, <c>false</c> otherwise</returns>
    public static bool IsPathTooLongException(this Exception? ex)
    {
        if (ex is PathTooLongException)
            return true;

        if (ex is Win32Exception win32Ex && OperatingSystem.IsWindows())
            return WindowsPathTooLongCodes.Contains(win32Ex.NativeErrorCode);

        if (ex is IOException ioEx)
        {
            var code = ioEx.HResult & 0xFFFF;

            if (OperatingSystem.IsWindows())
                return WindowsPathTooLongCodes.Contains(code);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (ioEx.InnerException is Win32Exception innerWin32 &&
                    PosixPathTooLongErrnos.Contains(innerWin32.NativeErrorCode))
                    return true;

                return PosixPathTooLongErrnos.Contains(code);
            }
        }

        return false;
    }
}