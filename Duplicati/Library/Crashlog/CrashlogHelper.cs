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
using System;
using System.IO;
using System.Threading.Tasks;

namespace Duplicati.Library.Crashlog;
#nullable enable

/// <summary>
/// Utility class to wrap a method in a try-catch block and log any exceptions to a file
/// </summary>
public static class CrashlogHelper
{
    /// <summary>
    /// The system temp path
    /// </summary>
    private static readonly string SystemTempPath
#if DEBUG
        = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location) ?? Path.GetTempPath();
#else
        = Path.GetTempPath();
#endif

    /// <summary>
    /// The default directory to write crashlogs to
    /// </summary>
    public static string DefaultLogDir { get; set; } = SystemTempPath;

    /// <summary>
    /// Event to subscribe to for unobserved task exceptions
    /// </summary>
    public static event Action<Exception>? OnUnobservedTaskException;

    /// <summary>
    /// Handler for unobserved task exceptions
    /// </summary>
    /// <param name="sender">Unused sender</param>
    /// <param name="e">The exception event args</param>
    private static void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            OnUnobservedTaskException?.Invoke(e.Exception);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Wraps a method in a try-catch block and logs any exceptions to a file
    /// </summary>
    /// <typeparam name="T">The return type of the method</typeparam>
    /// <param name="method">The method to wrap</param>
    /// <returns>The result of the method</returns>
    public static T WrapWithCrashLog<T>(Func<T> method)
    {
        try
        {
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
            return method();
        }
        catch (Exception ex)
        {
            LogCrashException(ex);
            throw;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= TaskSchedulerOnUnobservedTaskException;
        }
    }

    /// <summary>
    /// Wraps a method in a try-catch block and logs any exceptions to a file
    /// </summary>
    /// <typeparam name="T">The return type of the method</typeparam>
    /// <param name="method">The method to wrap</param>
    /// <returns>The result of the method</returns>
    public static async Task<T> WrapWithCrashLog<T>(Func<Task<T>> method)
    {
        try
        {
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
            return await method().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCrashException(ex);
            throw;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= TaskSchedulerOnUnobservedTaskException;
        }
    }

    /// <summary>
    /// The application name
    /// </summary>
    private static readonly string ApplicationName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? Guid.NewGuid().ToString()[..8];

    /// <summary>
    /// Gets the path to the crashlog file
    /// </summary>
    /// <returns>The path to the crashlog file</returns>
    private static string GetLogFilePath()
    {
        var logdir = string.IsNullOrWhiteSpace(DefaultLogDir)
            ? SystemTempPath
            : DefaultLogDir;

        return Path.Combine(logdir, $"{ApplicationName}-crashlog.txt");
    }

    /// <summary>
    /// Gets the last crashlog, if any
    /// </summary>
    /// <returns>The last crashlog, or null if none</returns>
    public static string? GetLastCrashLog()
    {
        try
        {
            var path = GetLogFilePath();
            if (!File.Exists(path))
                return null;
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Logs the exception to a file
    /// </summary>
    /// <param name="ex">The exception to log</param>
    public static void LogCrashException(Exception ex)
    {
        try
        {
            Console.WriteLine("Crash! {0}{1}", Environment.NewLine, ex);
        }
        catch
        {
        }

        try
        {
            File.WriteAllText(GetLogFilePath(), ex.ToString());
        }
        catch (Exception writeex)
        {
            try
            {
                Console.WriteLine("Failed to write crashlog: {0}", writeex);
            }
            catch
            {
            }
        }
    }
}
