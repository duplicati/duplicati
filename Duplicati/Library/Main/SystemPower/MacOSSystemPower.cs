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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.SystemPower;

/// <summary>
/// Shuts down or restarts a macOS computer.
/// A user in a graphical session asks the login window with an Apple Event, as the Apple menu does.
/// macOS has no such interface for a process outside a graphical session, so root uses the system's shutdown program.
/// </summary>
[SupportedOSPlatform("macos")]
public static class MacOSSystemPower
{
    private const string CORESERVICES = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string SECURITY = "/System/Library/Frameworks/Security.framework/Security";

    /// <summary>
    /// The shutdown program, which only root can use. It is only started from this fixed location.
    /// </summary>
    public const string ShutdownProgram = "/sbin/shutdown";

    /// <summary>
    /// The time to wait for the shutdown program to accept the request
    /// </summary>
    private static readonly TimeSpan ProgramTimeout = TimeSpan.FromSeconds(30);

    /// <summary><c>kCoreEventClass</c>, 'aevt'</summary>
    private const uint kCoreEventClass = 0x61657674;
    /// <summary><c>kAEShutDown</c>, 'shut'</summary>
    private const uint kAEShutDown = 0x73687574;
    /// <summary><c>kAERestart</c>, 'rest'</summary>
    private const uint kAERestart = 0x72657374;
    /// <summary><c>typeProcessSerialNumber</c>, 'psn '</summary>
    private const uint typeProcessSerialNumber = 0x70736E20;
    /// <summary><c>kSystemProcess</c>: the process serial number that addresses the login window</summary>
    private const uint kSystemProcess = 1;
    /// <summary><c>typeNull</c>, 'null'</summary>
    private const int typeNull = 0x6E756C6C;
    private const short kAutoGenerateReturnID = -1;
    private const int kAnyTransactionID = 0;
    private const int kAENoReply = 0x00000001;
    private const nint kAEDefaultTimeout = -1;
    /// <summary><c>callerSecuritySession</c>: the session of the calling process</summary>
    private const uint callerSecuritySession = 0xFFFFFFFF;
    /// <summary><c>sessionHasGraphicAccess</c>: the session can reach the window server</summary>
    private const uint sessionHasGraphicAccess = 0x0010;

    /// <summary>
    /// The space reserved for an <c>AEDesc</c>, which is a four byte type followed by a pointer.
    /// The descriptors are only handed back to the Apple Event functions, so they are kept as opaque
    /// memory that is large enough whether or not the structure is padded.
    /// </summary>
    private const int AEDESC_SIZE = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessSerialNumber
    {
        public uint HighLongOfPSN;
        public uint LowLongOfPSN;
    }

    [DllImport(CORESERVICES)]
    private static extern short AECreateDesc(uint typeCode, ref ProcessSerialNumber data, nint dataSize, IntPtr result);

    [DllImport(CORESERVICES)]
    private static extern short AECreateAppleEvent(uint eventClass, uint eventId, IntPtr target, short returnId, int transactionId, IntPtr result);

    [DllImport(CORESERVICES)]
    private static extern int AESendMessage(IntPtr appleEvent, IntPtr reply, int sendMode, nint timeoutInTicks);

    [DllImport(CORESERVICES)]
    private static extern short AEDisposeDesc(IntPtr descriptor);

    [DllImport(SECURITY)]
    private static extern int SessionGetInfo(uint session, out uint sessionId, out uint attributes);

    /// <summary>
    /// Checks if the process runs in a session with access to the graphical login session
    /// </summary>
    /// <returns>True if the login window can be reached</returns>
    public static bool HasGraphicSession()
        => SessionGetInfo(callerSecuritySession, out _, out var attributes) == 0
            && (attributes & sessionHasGraphicAccess) != 0;

    /// <summary>
    /// Checks if the computer can be shut down or restarted by this process
    /// </summary>
    /// <param name="isPrivileged">True if the process runs as root</param>
    /// <returns>True if the action can be performed</returns>
    public static bool CanPerform(bool isPrivileged)
        => isPrivileged ? File.Exists(ShutdownProgram) : HasGraphicSession();

    /// <summary>
    /// Shuts down or restarts the computer
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="isPrivileged">True if the process runs as root</param>
    /// <param name="delayMinutes">The time to wait before shutting down, which only root can ask for</param>
    /// <param name="message">The message shown to the logged in users, which only root can send</param>
    /// <param name="cancellationToken">The token that cancels waiting for the request to be accepted</param>
    public static async Task PerformAsync(bool reboot, bool isPrivileged, int delayMinutes, string message, CancellationToken cancellationToken)
    {
        if (isPrivileged)
            await RunShutdownProgramAsync(reboot, delayMinutes, message, cancellationToken).ConfigureAwait(false);
        else
            WithLoginWindowEvent(reboot, SendEvent);
    }

    /// <summary>
    /// Builds the Apple Event that asks the login window to shut down or restart, without sending it
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    public static void VerifyLoginWindowEvent(bool reboot)
        => WithLoginWindowEvent(reboot, _ => { });

    /// <summary>
    /// Sends an Apple Event without waiting for a reply
    /// </summary>
    /// <param name="appleEvent">The event to send</param>
    private static void SendEvent(IntPtr appleEvent)
    {
        var reply = Marshal.AllocHGlobal(AEDESC_SIZE);
        try
        {
            // An empty descriptor: the null type and no data
            Marshal.Copy(new byte[AEDESC_SIZE], 0, reply, AEDESC_SIZE);
            Marshal.WriteInt32(reply, typeNull);
            var status = AESendMessage(appleEvent, reply, kAENoReply, kAEDefaultTimeout);
            if (status != 0)
                throw new InvalidOperationException($"The login window did not accept the request, error code {status}");
            AEDisposeDesc(reply);
        }
        finally
        {
            Marshal.FreeHGlobal(reply);
        }
    }

    /// <summary>
    /// Builds the Apple Event that asks the login window to shut down or restart, and hands it to a callback
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="callback">The callback that uses the event, which is released when the callback returns</param>
    private static void WithLoginWindowEvent(bool reboot, Action<IntPtr> callback)
    {
        var target = Marshal.AllocHGlobal(AEDESC_SIZE);
        var appleEvent = Marshal.AllocHGlobal(AEDESC_SIZE);
        try
        {
            var loginWindow = new ProcessSerialNumber { HighLongOfPSN = 0, LowLongOfPSN = kSystemProcess };
            var error = AECreateDesc(typeProcessSerialNumber, ref loginWindow, Marshal.SizeOf<ProcessSerialNumber>(), target);
            if (error != 0)
                throw new InvalidOperationException($"Failed to address the login window, error code {error}");

            try
            {
                error = AECreateAppleEvent(kCoreEventClass, reboot ? kAERestart : kAEShutDown, target, kAutoGenerateReturnID, kAnyTransactionID, appleEvent);
                if (error != 0)
                    throw new InvalidOperationException($"Failed to create the request for the login window, error code {error}");

                try
                {
                    callback(appleEvent);
                }
                finally
                {
                    AEDisposeDesc(appleEvent);
                }
            }
            finally
            {
                AEDisposeDesc(target);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(appleEvent);
            Marshal.FreeHGlobal(target);
        }
    }

    /// <summary>
    /// Builds the arguments for the shutdown program
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="delayMinutes">The time to wait before shutting down</param>
    /// <param name="message">The message shown to the logged in users</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> GetShutdownProgramArguments(bool reboot, int delayMinutes, string message)
    {
        return [reboot ? "-r" : "-h", $"+{delayMinutes}", message];
    }

    /// <summary>
    /// Runs the shutdown program, which is the only way to shut down outside a graphical session
    /// </summary>
    private static async Task RunShutdownProgramAsync(bool reboot, int delayMinutes, string message, CancellationToken cancellationToken)
    {
        // Only the error stream is redirected, and it is only read after a failure:
        // the shutdown program stays in the background and keeps the stream open
        var startInfo = new ProcessStartInfo(ShutdownProgram)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in GetShutdownProgramArguments(reboot, delayMinutes, message))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {ShutdownProgram}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProgramTimeout);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = (await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false)).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"{ShutdownProgram} failed with exit code {process.ExitCode}"
                : $"{ShutdownProgram} failed with exit code {process.ExitCode}: {error}");
        }
    }
}
