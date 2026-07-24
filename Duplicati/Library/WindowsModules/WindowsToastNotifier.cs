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
using Duplicati.Library.Interface;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Shows notifications as Windows toast notifications.
/// The compat layer in the toolkit handles the AppUserModelID and COM activator
/// registration required for an unpackaged desktop application.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsToastNotifier : INativeNotifier
{
    /// <inheritdoc/>
    public Action? NotificationClicked { get; set; }

    /// <summary>
    /// Creates a new toast notifier and hooks up activation handling.
    /// </summary>
    public WindowsToastNotifier()
    {
        // The activation handler must be registered before the first toast is
        // shown. The callback arrives on a COM thread, not the UI thread.
        ToastNotificationManagerCompat.OnActivated += _ => NotificationClicked?.Invoke();
    }

    /// <inheritdoc/>
    public void Notify(NativeNotificationLevel level, string title, string message)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .Show();
    }
}
