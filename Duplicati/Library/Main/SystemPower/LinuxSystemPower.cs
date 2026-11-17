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
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Duplicati.Library.Main.SystemPower;

/// <summary>
/// Shuts down or restarts a Linux computer through the login manager (systemd-logind or elogind) on the system bus.
/// The login manager decides with polkit if the caller may do so, which by default
/// allows root and a user in the active local session.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxSystemPower
{
    private const string DESTINATION = "org.freedesktop.login1";
    private const string PATH = "/org/freedesktop/login1";
    private const string INTERFACE = "org.freedesktop.login1.Manager";

    /// <summary>
    /// The time to wait for the login manager to respond
    /// </summary>
    private static readonly TimeSpan DBusTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Asks the login manager if the caller can perform the action without being asked to authenticate
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="cancellationToken">The token that cancels the request</param>
    /// <returns>True if the action can be performed</returns>
    public static async Task<bool> CanPerformAsync(bool reboot, CancellationToken cancellationToken)
    {
        var connection = DBusConnection.System;
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: DESTINATION,
            path: PATH,
            @interface: INTERFACE,
            member: reboot ? "CanReboot" : "CanPowerOff");

        // The other answers are "no", "na" for unsupported hardware, and "challenge",
        // which needs a person to authenticate
        var answer = await connection.CallMethodAsync(writer.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadString(), null)
            .WaitAsync(DBusTimeout, cancellationToken).ConfigureAwait(false);
        return string.Equals(answer, "yes", StringComparison.Ordinal);
    }

    /// <summary>
    /// Asks the login manager to shut down or restart after a delay, during which the logged in users see a warning
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="delay">The time the login manager waits before it shuts down</param>
    /// <param name="cancellationToken">The token that cancels the request</param>
    public static async Task PerformAsync(bool reboot, TimeSpan delay, CancellationToken cancellationToken)
    {
        var connection = DBusConnection.System;
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: DESTINATION,
            path: PATH,
            @interface: INTERFACE,
            signature: "st",
            member: "ScheduleShutdown");
        writer.WriteString(reboot ? "reboot" : "poweroff");
        // The time is in microseconds since the Unix epoch
        writer.WriteUInt64((ulong)(DateTimeOffset.UtcNow + delay).ToUnixTimeMilliseconds() * 1000);

        await connection.CallMethodAsync(writer.CreateMessage())
            .WaitAsync(DBusTimeout, cancellationToken).ConfigureAwait(false);
    }
}
