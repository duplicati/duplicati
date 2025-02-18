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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ReleaseBuilder;

/// <summary>
/// A class that prevents the system from sleeping during build
/// </summary>
public class KeepAliveAssertion : IDisposable
{
    /// <summary>
    /// The previous state of the system, on Windows
    /// </summary>
    private NativeMethods.EXECUTION_STATE _previousState;
    /// <summary>
    /// The process that keeps the system awake, on Linux and MacOS
    /// </summary>
    private Process? _keepAliveProcess;

    /// <summary>
    /// Creates a new instance of the <see cref="KeepAliveAssertion"/> class
    /// </summary>
    public KeepAliveAssertion()
    {
        if (OperatingSystem.IsWindows())
        {
            _previousState = NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS | NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED | NativeMethods.EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
        }
        else if (OperatingSystem.IsLinux())
        {
            _keepAliveProcess = Process.Start("xset", "s off -dpms");
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Keep the display awake until this process exits
            var selfProcessId = Process.GetCurrentProcess().Id;
            _keepAliveProcess = Process.Start("caffeinate", ["-d", "-w", selfProcessId.ToString()]);
        }
    }

    /// <summary>
    /// A class that contains Windows native methods
    /// </summary>
    [SupportedOSPlatform("windows")]
    private class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags); // returns the previous state

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_SYSTEM_REQUIRED = 0x00000001
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.SetThreadExecutionState(_previousState);
        }
        else if (OperatingSystem.IsLinux())
        {
            _keepAliveProcess?.Kill();
            _keepAliveProcess?.Dispose();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _keepAliveProcess?.Kill();
            _keepAliveProcess?.Dispose();
        }
    }
}
