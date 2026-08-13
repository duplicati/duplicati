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
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using RumpSharp;

namespace Duplicati.GUI.TrayIcon;

/// <summary>
/// Shows notifications in the macOS Notification Center through RumpSharp,
/// which talks to <c>UNUserNotificationCenter</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// The constructor must run on the UI/main thread once the application has
/// created its <c>NSApplication</c> (for Avalonia, after framework
/// initialization): when the process posts in-process, creating the center
/// calls <c>+[NSApplication sharedApplication]</c> and installs the delegate
/// that receives clicks, and AppKit only allows that on the main thread.
/// </para>
/// <para>
/// When Duplicati runs from its own <c>.app</c> bundle the notification is
/// posted in-process with Duplicati's identity; a bare executable posts
/// through RumpSharp's bundled helper, which
/// <see cref="AppBundle.PrepareIfNeeded"/> names as Duplicati. The helper
/// bundle and the permission prompt only appear once a notification is
/// actually sent.
/// </para>
/// </remarks>
[SupportedOSPlatform("macOS")]
public sealed class MacOSRumpNotifier : INativeNotifier
{
    private static readonly string LOGTAG = Log.LogTagFromType<MacOSRumpNotifier>();

    /// <summary>
    /// The bundle identifier used for the helper bundle when Duplicati is not
    /// running from its own <c>.app</c> bundle. Notification permission is
    /// granted per identifier, so this must stay stable across releases.
    /// This intentionally differs from the shipped bundle's identifier
    /// (<c>com.duplicati.app</c>): macOS keys the notification icon off the
    /// bundle identifier, and reusing the real one makes Notification Center
    /// resolve the icon from any other installed Duplicati bundle instead of
    /// the helper's.
    /// </summary>
    private const string HelperBundleIdentifier = "com.duplicati.app.notificationhelper";

    /// <summary>
    /// The RumpSharp notification center this notifier posts through
    /// </summary>
    private readonly NotificationCenter notificationCenter;

    /// <summary>
    /// Creates the notifier and the RumpSharp notification center; call on the
    /// UI/main thread (see the class remarks)
    /// </summary>
    public MacOSRumpNotifier()
    {
        string? iconPath = null;
        if (!AppBundle.IsBundled)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Duplicati.icns");
            if (File.Exists(path))
                iconPath = path;
        }

        // Give the process a bundle identity if it has none. When Duplicati
        // runs from its own .app bundle this does nothing and notifications
        // are posted in-process; otherwise it prepares the helper bundle
        // whose name is what the user sees on the banners.
        AppBundle.PrepareIfNeeded(new AppBundleOptions
        {
            Name = "Duplicati",
            BundleIdentifier = HelperBundleIdentifier,
            ShowInDock = false,
            IconPath = iconPath,
        });

        notificationCenter = new NotificationCenter();

        // Clicks arrive on the main thread's run loop when posting
        // in-process, and on a background thread when posting through the
        // helper; INativeNotifier allows either.
        notificationCenter.Activated += (_, response) =>
        {
            if (response.Activation == NotificationActivation.Default)
                NotificationClicked?.Invoke();
        };

        // Never block the UI thread on the permission prompt.
        Task.Run(() => notificationCenter.RequestAuthorization());
    }

    /// <inheritdoc/>
    public Action? NotificationClicked { get; set; }

    /// <inheritdoc/>
    public void Notify(NativeNotificationLevel level, string title, string message)
    {
        // macOS notifications have no severity concept, so the level is
        // ignored. The first Show may wait out the permission prompt, and
        // every Show blocks until macOS has accepted the notification, so the
        // work runs on a background thread to never stall the caller's thread
        // (usually the UI thread). As there is no caller left when the work
        // finishes, failures are logged here instead of being thrown.
        Task.Run(() =>
        {
            try
            {
                notificationCenter.Show(title, null, message);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "NotificationFailed", ex, "Failed to show notification: {0}", title);
            }
        });
    }
}
