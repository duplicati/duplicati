
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
using System.Threading;
using Duplicati.Library.Interface;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Implementation of a power mode provider using a hidden window to receive power broadcast messages
/// </summary>
class PowerManagementModule : IPowerModeProvider
{
    /// <summary>
    /// The message loop thread
    /// </summary>
    private readonly Thread _messageThread;
    /// <summary>
    /// Event to signal that initialization is complete
    /// </summary>
    private readonly ManualResetEvent _init = new(false);
    /// <summary>
    /// The window handle for the hidden window
    /// </summary>
    private HWND _hwnd;

    /// <summary>
    /// Event that is triggered when the system is resuming from suspend
    /// </summary>
    public Action? OnResume { get; set; }
    /// <summary>
    /// Event that is triggered when the system is suspending
    /// </summary>
    public Action? OnSuspend { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerManagementModule"/> class.
    /// </summary>
    public PowerManagementModule()
    {
        _messageThread = new Thread(MessageLoop) { IsBackground = true };
        _messageThread.Start();
        _init.WaitOne();
    }

    /// <summary>
    /// The message loop for the hidden window
    /// </summary>
    private void MessageLoop()
    {
        _hwnd = CreateWindowEx(
            0, "STATIC", "PowerMonitorWnd",
            WindowStyles.WS_OVERLAPPED,
            0, 0, 0, 0, HWND.NULL, HMENU.NULL, HINSTANCE.NULL, IntPtr.Zero);

        _init.Set();

        MSG msg;
        while (GetMessage(out msg, HWND.NULL, 0, 0) > 0)
        {
            if (msg.message == (uint)WindowMessage.WM_POWERBROADCAST)
            {
                switch ((PowerBroadcastType)msg.wParam)
                {
                    case PowerBroadcastType.PBT_APMSUSPEND:
                        OnSuspend?.Invoke();
                        break;
                    case PowerBroadcastType.PBT_APMRESUMEAUTOMATIC:
                    case PowerBroadcastType.PBT_APMRESUMESUSPEND:
                        OnResume?.Invoke();
                        break;
                }
            }
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }
    }

    /// <summary>
    /// Disposes the power mode provider and stops listening for events
    /// </summary>
    public void Dispose()
    {
        if (_hwnd != HWND.NULL)
        {
            PostMessage(_hwnd, (uint)WindowMessage.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _messageThread.Join();
        }
    }
}