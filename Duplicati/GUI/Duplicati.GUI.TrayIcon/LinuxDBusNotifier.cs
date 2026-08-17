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
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Tmds.DBus.Protocol;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.GUI.TrayIcon;

/// <summary>
/// Shows notifications through the Linux desktop notification service
/// (<c>org.freedesktop.Notifications</c>) over the session D-Bus.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is lightweight: it only captures the session bus connection
/// object. The first call to <see cref="Notify"/> performs the actual D-Bus
/// round-trip and subscribes to <c>ActionInvoked</c> signals, all on a
/// background thread so the caller (usually the UI thread) is never blocked.
/// </para>
/// <para>
/// Clicking the notification body fires <see cref="NotificationClicked"/>
/// via the <c>"default"</c> action. The callback arrives on the D-Bus
/// event-loop thread, which <see cref="INativeNotifier.NotificationClicked"/>
/// explicitly allows.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxDBusNotifier : INativeNotifier
{
    private static readonly string LOGTAG = Log.LogTagFromType<LinuxDBusNotifier>();

    private const string ServiceName = "org.freedesktop.Notifications";
    private const string ServicePath = "/org/freedesktop/Notifications";
    private const string Interface = "org.freedesktop.Notifications";

    private readonly DBusConnection connection;

    /// <summary>
    /// Guards the one-time signal subscription.
    /// </summary>
    private int signalSubscriptionStarted;

    /// <summary>
    /// Disposable for the ActionInvoked match rule.
    /// </summary>
    private IDisposable? actionInvokedSubscription;

    /// <inheritdoc/>
    public Action? NotificationClicked { get; set; }

    /// <summary>
    /// Creates a new notifier backed by the session bus.
    /// Throws if no session bus is available.
    /// </summary>
    public LinuxDBusNotifier()
    {
        connection = DBusConnection.Session;
    }

    /// <inheritdoc/>
    public void Notify(NativeNotificationLevel level, string title, string message)
    {
        // All D-Bus I/O happens on a background thread so the UI thread
        // (which is the typical caller) is never blocked.
        Task.Run(() => NotifyCoreAsync(level, title, message));
    }

    private async Task NotifyCoreAsync(NativeNotificationLevel level, string title, string message)
    {
        try
        {
            EnsureSignalSubscription();

            var urgency = level switch
            {
                NativeNotificationLevel.Warning => (byte)1,
                NativeNotificationLevel.Error => (byte)2,
                _ => (byte)0,
            };

            var icon = level switch
            {
                NativeNotificationLevel.Warning => "dialog-warning",
                NativeNotificationLevel.Error => "dialog-error",
                _ => "duplicati",
            };

            var hints = new Dictionary<string, VariantValue>
            {
                ["urgency"] = VariantValue.Byte(urgency),
            };

            // "default" action lets the server report clicks on the banner body
            var actions = new[] { "default", "Open Duplicati" };

            await CallNotifyAsync(
                appName: "Duplicati",
                replacesId: 0,
                appIcon: icon,
                summary: title,
                body: message,
                actions: actions,
                hints: hints,
                expireTimeout: -1
            ).ConfigureAwait(false);

            Log.WriteVerboseMessage(LOGTAG, "NotificationShown", "Notification shown: {0}", title);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "NotificationFailed", ex, "Failed to show notification: {0}", title);
        }
    }

    /// <summary>
    /// Subscribes to <c>ActionInvoked</c> signals exactly once.
    /// </summary>
    private void EnsureSignalSubscription()
    {
        if (Interlocked.CompareExchange(ref signalSubscriptionStarted, 1, 0) != 0)
            return;

        try
        {
            var rule = new MatchRule
            {
                Type = MessageType.Signal,
                Sender = ServiceName,
                Path = ServicePath,
                Interface = Interface,
                Member = "ActionInvoked",
            };

            actionInvokedSubscription = connection.AddMatchAsync(
                rule,
                (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    return (id: reader.ReadUInt32(), key: reader.ReadString());
                },
                (Action<Notification<(uint id, string key)>>)(n =>
                    HandleActionInvoked(n.Exception, n.HasValue ? n.Value : default, NotificationClicked)),
                false, ObserverFlags.None, null
            ).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "SignalSubscribeFailed", ex, "Failed to subscribe to ActionInvoked");
        }
    }

    /// <summary>
    /// Decides what an <c>ActionInvoked</c> signal means. Kept separate from the
    /// subscription so it can be exercised without a session bus, which the notifier
    /// itself requires to exist.
    /// </summary>
    /// <param name="exception">The error the subscription reported, if any.</param>
    /// <param name="action">The signal payload: the notification id and the action key.</param>
    /// <param name="notificationClicked">The callback to raise when the body of the notification was clicked.</param>
    internal static void HandleActionInvoked(Exception? exception, (uint id, string key) action, Action? notificationClicked)
    {
        if (exception != null)
            Log.WriteWarningMessage(LOGTAG, "SignalError", exception, "Error in ActionInvoked signal");
        else if (action.key == "default")
            notificationClicked?.Invoke();
    }

    private Task<uint> CallNotifyAsync(
        string appName, uint replacesId, string appIcon,
        string summary, string body, string[] actions,
        Dictionary<string, VariantValue> hints, int expireTimeout)
    {
        return connection.CallMethodAsync(BuildMessage(), (Message m, object? _) =>
        {
            var reader = m.GetBodyReader();
            return reader.ReadUInt32();
        }, (object?)null);

        MessageBuffer BuildMessage()
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: ServiceName,
                path: ServicePath,
                @interface: Interface,
                signature: "susssasa{sv}i",
                member: "Notify");
            writer.WriteString(appName);
            writer.WriteUInt32(replacesId);
            writer.WriteString(appIcon);
            writer.WriteString(summary);
            writer.WriteString(body);
            writer.WriteArray(actions);
            writer.WriteDictionary(hints);
            writer.WriteInt32(expireTimeout);
            return writer.CreateMessage();
        }
    }
}
