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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Duplicati.Library.Interface;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Provides power management functionality for Windows, monitoring system suspend and resume events.
/// Implements IPowerModeProvider to notify about power state changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerManagementModule : IPowerModeProvider, IDisposable
{
    /// <summary>
    /// The background thread that runs the message loop for handling Windows messages.
    /// </summary>
    private readonly Thread _thread;

    /// <summary>
    /// Manual reset event used to signal when the message loop initialization is complete.
    /// </summary>
    private readonly ManualResetEvent _init = new(false);

    /// <summary>
    /// Handle to the hidden window used for receiving power broadcast messages.
    /// </summary>
    private IntPtr _hwnd = IntPtr.Zero;

    /// <summary>
    /// Reference to the window procedure delegate to prevent garbage collection.
    /// </summary>
    private WndProc? _wndProc; // keep delegate alive

    /// <summary>
    /// Handle to the power setting notification registration.
    /// </summary>
    private IntPtr _powerNotifyHandle = IntPtr.Zero;

    /// <summary>
    /// Gets or sets the action to invoke when the system resumes from suspend.
    /// </summary>
    public Action? OnResume { get; set; }

    /// <summary>
    /// Gets or sets the action to invoke when the system is about to suspend.
    /// </summary>
    public Action? OnSuspend { get; set; }

    /// <summary>
    /// Initializes a new instance of the PowerManagementModule class with default settings.
    /// Required for reflection-based loading.
    /// </summary>
    public PowerManagementModule() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PowerManagementModule class.
    /// </summary>
    /// <param name="powerSettingToSubscribe">Optional GUID for a specific power setting to subscribe to, or null to skip subscription.</param>
    public PowerManagementModule(Guid? powerSettingToSubscribe = null)
    {
        _thread = new Thread(() => MessageLoop(powerSettingToSubscribe)) { IsBackground = true };
        _thread.Start();
        _init.WaitOne();
    }

    /// <summary>
    /// Runs the message loop for handling Windows messages, including power broadcast events.
    /// Creates a hidden window and optionally subscribes to power setting notifications.
    /// </summary>
    /// <param name="subscribeGuid">Optional GUID for power setting subscription.</param>
    private void MessageLoop(Guid? subscribeGuid)
    {
        _wndProc = WndProcImpl;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            lpszClassName = "PowerMonitorWndClass",
            hInstance = GetModuleHandle(null)
        };
        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            _init.Set();
            return;
        }

        // Hidden, message-only window
        _hwnd = CreateWindowEx(
            0,
            wc.lpszClassName,
            "PowerMonitorWnd",
            0,
            0, 0, 0, 0,
            HWND_MESSAGE,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            _init.Set();
            return;
        }

        if (subscribeGuid.HasValue)
        {
            var guid = subscribeGuid.Value;
            _powerNotifyHandle = RegisterPowerSettingNotification(_hwnd, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        _init.Set();

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_powerNotifyHandle != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotifyHandle);
            _powerNotifyHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Window procedure implementation that handles power broadcast messages.
    /// Processes suspend and resume events, invoking the appropriate actions.
    /// </summary>
    /// <param name="hwnd">Handle to the window.</param>
    /// <param name="msg">Message identifier.</param>
    /// <param name="wParam">Additional message information.</param>
    /// <param name="lParam">Additional message information.</param>
    /// <returns>Result of message processing.</returns>
    private IntPtr WndProcImpl(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == WM_POWERBROADCAST)
        {
            var evt = (uint)wParam.ToUInt64();
            switch (evt)
            {
                case PBT_APMSUSPEND:
                    OnSuspend?.Invoke();
                    return new IntPtr(1); // processed

                case PBT_APMRESUMEAUTOMATIC:
                case PBT_APMRESUMESUSPEND:
                    OnResume?.Invoke();
                    return new IntPtr(1);

                case PBT_POWERSETTINGCHANGE:
                    // Ignored by default; can be extended if needed.
                    return new IntPtr(1);
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Disposes of the PowerManagementModule, cleaning up resources and stopping the message loop.
    /// </summary>
    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join();
            _hwnd = IntPtr.Zero;
        }

        if (_powerNotifyHandle != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotifyHandle);
            _powerNotifyHandle = IntPtr.Zero;
        }

        _init.Dispose();
    }

    // Interop

    /// <summary>
    /// Windows message for power broadcast events.
    /// </summary>
    private const uint WM_POWERBROADCAST = 0x0218;

    /// <summary>
    /// Windows message for window close.
    /// </summary>
    private const uint WM_CLOSE = 0x0010;

    /// <summary>
    /// Windows message for window destruction.
    /// </summary>
    private const uint WM_DESTROY = 0x0002;

    /// <summary>
    /// Power broadcast event for system suspend.
    /// </summary>
    private const uint PBT_APMSUSPEND = 0x0004;

    /// <summary>
    /// Power broadcast event for automatic resume from suspend.
    /// </summary>
    private const uint PBT_APMRESUMEAUTOMATIC = 0x0012;

    /// <summary>
    /// Power broadcast event for resume from suspend.
    /// </summary>
    private const uint PBT_APMRESUMESUSPEND = 0x0007;

    /// <summary>
    /// Power broadcast event for power setting change.
    /// </summary>
    private const uint PBT_POWERSETTINGCHANGE = 0x8013;

    /// <summary>
    /// Flag for registering power setting notification with a window handle.
    /// </summary>
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    /// <summary>
    /// Handle to the message-only window.
    /// </summary>
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    /// <summary>
    /// Delegate for the window procedure function that processes window messages.
    /// </summary>
    /// <param name="hWnd">Handle to the window.</param>
    /// <param name="msg">Message identifier.</param>
    /// <param name="wParam">Additional message information.</param>
    /// <param name="lParam">Additional message information.</param>
    /// <returns>Result of message processing.</returns>
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Represents the window class structure used for registering a window class.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        /// <summary>
        /// The size, in bytes, of this structure.
        /// </summary>
        public uint cbSize;

        /// <summary>
        /// The class style(s).
        /// </summary>
        public uint style;

        /// <summary>
        /// A pointer to the window procedure.
        /// </summary>
        public WndProc lpfnWndProc;

        /// <summary>
        /// The number of extra bytes to allocate following the window-class structure.
        /// </summary>
        public int cbClsExtra;

        /// <summary>
        /// The number of extra bytes to allocate following the window instance.
        /// </summary>
        public int cbWndExtra;

        /// <summary>
        /// A handle to the instance that contains the window procedure for the class.
        /// </summary>
        public IntPtr hInstance;

        /// <summary>
        /// A handle to the class icon.
        /// </summary>
        public IntPtr hIcon;

        /// <summary>
        /// A handle to the class cursor.
        /// </summary>
        public IntPtr hCursor;

        /// <summary>
        /// A handle to the class background brush.
        /// </summary>
        public IntPtr hbrBackground;

        /// <summary>
        /// Pointer to a null-terminated character string that specifies the resource name of the class menu.
        /// </summary>
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;

        /// <summary>
        /// A pointer to a null-terminated string or is an atom.
        /// </summary>
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;

        /// <summary>
        /// A handle to a small icon that is associated with the window class.
        /// </summary>
        public IntPtr hIconSm;
    }

    /// <summary>
    /// Represents a point with x and y coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        /// <summary>
        /// The x-coordinate of the point.
        /// </summary>
        public int x;

        /// <summary>
        /// The y-coordinate of the point.
        /// </summary>
        public int y;
    }

    /// <summary>
    /// Represents a Windows message structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        /// <summary>
        /// A handle to the window whose window procedure receives the message.
        /// </summary>
        public IntPtr hwnd;

        /// <summary>
        /// The message identifier.
        /// </summary>
        public uint message;

        /// <summary>
        /// Additional message information.
        /// </summary>
        public UIntPtr wParam;

        /// <summary>
        /// Additional message information.
        /// </summary>
        public IntPtr lParam;

        /// <summary>
        /// The time at which the message was posted.
        /// </summary>
        public uint time;

        /// <summary>
        /// The cursor position, in screen coordinates, when the message was posted.
        /// </summary>
        public POINT pt;
    }

    /// <summary>
    /// Registers a window class for subsequent use in calls to the CreateWindowEx function.
    /// </summary>
    /// <param name="lpwcx">Pointer to a WNDCLASSEX structure containing the class information.</param>
    /// <returns>If the function succeeds, the return value is a class atom that uniquely identifies the class being registered.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    /// <summary>
    /// Creates an overlapped, pop-up, or child window with an extended window style.
    /// </summary>
    /// <param name="dwExStyle">The extended window style of the window being created.</param>
    /// <param name="lpClassName">A null-terminated string or a class atom created by a previous call to RegisterClassEx.</param>
    /// <param name="lpWindowName">The window name.</param>
    /// <param name="dwStyle">The style of the window being created.</param>
    /// <param name="X">The initial horizontal position of the window.</param>
    /// <param name="Y">The initial vertical position of the window.</param>
    /// <param name="nWidth">The width, in device units, of the window.</param>
    /// <param name="nHeight">The height, in device units, of the window.</param>
    /// <param name="hWndParent">A handle to the parent or owner window of the window being created.</param>
    /// <param name="hMenu">A handle to a menu, or specifies a child-window identifier.</param>
    /// <param name="hInstance">A handle to the instance of the module to be associated with the window.</param>
    /// <param name="lpParam">Pointer to a value to be passed to the window through the CREATESTRUCT structure.</param>
    /// <returns>If the function succeeds, the return value is a handle to the new window.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    /// <summary>
    /// Retrieves a module handle for the specified module.
    /// </summary>
    /// <param name="lpModuleName">The name of the loaded module (either a .dll or .exe file).</param>
    /// <returns>If the function succeeds, the return value is a handle to the specified module.</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// Retrieves a message from the calling thread's message queue.
    /// </summary>
    /// <param name="lpMsg">Pointer to an MSG structure that receives message information.</param>
    /// <param name="hWnd">Handle to the window whose messages are to be retrieved.</param>
    /// <param name="wMsgFilterMin">The integer value of the lowest message value to be retrieved.</param>
    /// <param name="wMsgFilterMax">The integer value of the highest message value to be retrieved.</param>
    /// <returns>If the function retrieves a message other than WM_QUIT, the return value is nonzero.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /// <summary>
    /// Translates virtual-key messages into character messages.
    /// </summary>
    /// <param name="lpMsg">Pointer to an MSG structure that contains message information retrieved from GetMessage.</param>
    /// <returns>If the message is translated, the return value is nonzero.</returns>
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    /// <summary>
    /// Dispatches a message to a window procedure.
    /// </summary>
    /// <param name="lpMsg">Pointer to an MSG structure that contains the message.</param>
    /// <returns>The return value specifies the value returned by the window procedure.</returns>
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    /// <summary>
    /// Calls the default window procedure to provide default processing for any window messages that an application does not process.
    /// </summary>
    /// <param name="hWnd">Handle to the window procedure that received the message.</param>
    /// <param name="uMsg">The message.</param>
    /// <param name="wParam">Additional message information.</param>
    /// <param name="lParam">Additional message information.</param>
    /// <returns>The return value is the result of the message processing and depends on the message.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Places a message in the message queue associated with the thread that created the specified window.
    /// </summary>
    /// <param name="hWnd">Handle to the window whose window procedure is to receive the message.</param>
    /// <param name="Msg">The message to be posted.</param>
    /// <param name="wParam">Additional message-specific information.</param>
    /// <param name="lParam">Additional message-specific information.</param>
    /// <returns>If the function succeeds, the return value is nonzero.</returns>
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Indicates to the system that a thread has made a request to terminate.
    /// </summary>
    /// <param name="nExitCode">The application exit code.</param>
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    /// <summary>
    /// Registers the application to receive power setting notifications for the specified power setting event.
    /// </summary>
    /// <param name="hRecipient">Handle to the window or service that will receive the notifications.</param>
    /// <param name="PowerSettingGuid">The GUID of the power setting for which notifications are to be sent.</param>
    /// <param name="Flags">Flags that specify the recipient and the type of notifications to send.</param>
    /// <returns>If the function succeeds, the return value is a handle to the registration.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, uint Flags);

    /// <summary>
    /// Unregisters the power setting notification.
    /// </summary>
    /// <param name="Handle">Handle to the registration returned by RegisterPowerSettingNotification.</param>
    /// <returns>If the function succeeds, the return value is nonzero.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);
}