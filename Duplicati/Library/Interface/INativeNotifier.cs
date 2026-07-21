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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// The severity of a native notification
    /// </summary>
    public enum NativeNotificationLevel
    {
        /// <summary>An informational message</summary>
        Information,
        /// <summary>A warning message</summary>
        Warning,
        /// <summary>An error message</summary>
        Error
    }

    /// <summary>
    /// Shows notifications through the operating system's notification facility
    /// </summary>
    public interface INativeNotifier
    {
        /// <summary>
        /// Invoked when the user clicks or otherwise activates a notification.
        /// May be invoked on a non-UI thread.
        /// </summary>
        Action? NotificationClicked { get; set; }

        /// <summary>
        /// Shows a notification to the user
        /// </summary>
        /// <param name="level">The severity of the notification</param>
        /// <param name="title">The notification title</param>
        /// <param name="message">The notification message</param>
        void Notify(NativeNotificationLevel level, string title, string message);
    }
}
