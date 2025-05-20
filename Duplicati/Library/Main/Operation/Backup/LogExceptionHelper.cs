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
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Backup;

/// <summary>
/// Helper class to log path exceptions in a common way
/// </summary>
public static class LogExceptionHelper
{
    /// <summary>
    /// Logs a path warning message, using exception detection for the appropriate message
    /// </summary>
    /// <param name="ex">The exception to log</param>
    /// <param name="logtag">The log tag to use for the message</param>
    /// <param name="id">The id to use for the message</param>
    /// <param name="path">The path to use for the message</param>
    /// <param name="message">The message to log</param>
    public static void LogCommonWarning(Exception? ex, string logtag, string id, string path, string message = "Failed to process path: {0}")
    {
        if (ex.IsPermissionDeniedException())
            Log.WriteWarningMessage(logtag, "PermissionDenied", ex, "Excluding path due to permission denied: {0}", path);
        else if (ex.IsFileLockedException())
            Log.WriteWarningMessage(logtag, "FileLocked", ex, "Excluding path due to file locked: {0}", path);
        else if (ex.IsPathNotFoundException())
            Log.WriteWarningMessage(logtag, "PathNotFound", ex, "Excluding path due to path not found: {0}", path);
        else if (ex.IsPathTooLongException())
            Log.WriteWarningMessage(logtag, "PathTooLong", ex, "Excluding path due to path too long: {0}", path);
        else
            Log.WriteWarningMessage(logtag, id, ex, message, path);

    }
}