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
using Duplicati.Library.Interface;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Provides power management functionality for Windows using the powrprof callback API.
/// Eliminates the hidden window by registering a suspend/resume callback (Windows 8+).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerManagementModule : IPowerModeProvider, IDisposable
{
    /// <summary>
    /// Registration handle returned from PowerRegisterSuspendResumeNotification.
    /// </summary>
    private IntPtr _registrationHandle = IntPtr.Zero;

    /// <summary>
    /// Keep a reference to the delegate to prevent it from being garbage collected.
    /// </summary>
    private DEVICE_NOTIFY_CALLBACK_ROUTINE? _callbackRef;

    /// <inheritdoc />
    public Action? OnResume { get; set; }

    /// <inheritdoc />
    public Action? OnSuspend { get; set; }

    /// <summary>
    /// Initializes a new instance. Required for reflection-based loading.
    /// </summary>
    public PowerManagementModule() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance. The parameter is ignored in this implementation.
    /// </summary>
    /// <param name="_">Unused. Present for compatibility with previous constructor.</param>
    public PowerManagementModule(Guid? _)
    {
        RegisterSuspendResumeCallback();
    }

    /// <summary>
    /// Registers the suspend/resume callback using powrprof (Windows 8+).
    /// </summary>
    private void RegisterSuspendResumeCallback()
    {
        _callbackRef = new DEVICE_NOTIFY_CALLBACK_ROUTINE(SuspendResumeCallback);
        var parameters = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = _callbackRef,
            Context = IntPtr.Zero
        };

        // DEVICE_NOTIFY_CALLBACK delivers notifications via the provided delegate.
        uint status = PowerRegisterSuspendResumeNotification(DEVICE_NOTIFY_CALLBACK, ref parameters, out _registrationHandle);

        // If registration fails, we keep a no-op provider (no window fallback by design).
        // STATUS_SUCCESS is 0.
        if (status != STATUS_SUCCESS)
        {
            _registrationHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Callback invoked by the system for suspend/resume notifications.
    /// </summary>
    /// <param name="context">User-provided context (unused).</param>
    /// <param name="type">Power event type (e.g., PBT_APMSUSPEND, PBT_APMRESUMEAUTOMATIC).</param>
    /// <param name="setting">Additional info (unused).</param>
    /// <returns>STATUS_SUCCESS (0) on success.</returns>
    private static uint STATUS_SUCCESS => 0;
    private uint SuspendResumeCallback(IntPtr context, uint type, IntPtr setting)
    {
        switch (type)
        {
            case PBT_APMSUSPEND:
                OnSuspend?.Invoke();
                break;

            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                OnResume?.Invoke();
                break;
        }

        return STATUS_SUCCESS;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_registrationHandle != IntPtr.Zero)
        {
            PowerUnregisterSuspendResumeNotification(_registrationHandle);
            _registrationHandle = IntPtr.Zero;
        }

        _callbackRef = null;
    }

    // Interop

    // Power broadcast event for system suspend.
    private const uint PBT_APMSUSPEND = 0x0004;
    // Power broadcast event for automatic resume from suspend.
    private const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    // Power broadcast event for resume from suspend.
    private const uint PBT_APMRESUMESUSPEND = 0x0007;

    // Flag indicating that the recipient is a callback routine.
    private const uint DEVICE_NOTIFY_CALLBACK = 2;

    /// <summary>
    /// Structure used to subscribe to suspend/resume notifications via callback.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public DEVICE_NOTIFY_CALLBACK_ROUTINE Callback;
        public IntPtr Context;
    }

    /// <summary>
    /// Callback routine signature for device/power notifications.
    /// Return STATUS_SUCCESS (0) on success.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DEVICE_NOTIFY_CALLBACK_ROUTINE(IntPtr Context, uint Type, IntPtr Setting);

    /// <summary>
    /// Registers to receive power suspend/resume notifications via a callback.
    /// </summary>
    /// <param name="Flags">Must be DEVICE_NOTIFY_CALLBACK for callback delivery.</param>
    /// <param name="Parameters">Callback and context parameters.</param>
    /// <param name="Handle">Out registration handle.</param>
    /// <returns>STATUS_SUCCESS (0) on success.</returns>
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerRegisterSuspendResumeNotification(
        uint Flags,
        ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS Parameters,
        out IntPtr Handle);

    /// <summary>
    /// Unregisters a previous suspend/resume notification registration.
    /// </summary>
    /// <param name="Handle">The registration handle.</param>
    /// <returns>STATUS_SUCCESS (0) on success.</returns>
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr Handle);
}