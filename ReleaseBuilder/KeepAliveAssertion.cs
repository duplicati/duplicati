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
