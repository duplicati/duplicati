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
using Duplicati.GUI.TrayIcon;
using Duplicati.Library.Snapshots.Windows;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Smoke tests for the native notifier implementations. Only the loading
    /// and the callback wiring are exercised; no notification is shown,
    /// because notification display depends on the session/OS configuration
    /// of the machine running the tests.
    /// </summary>
    [TestFixture]
    [Category("NativeNotifier")]
    public class NativeNotifierTests
    {
        [Test]
        public void NativeNotifierLoadsThroughTheShim()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("The native notifier is only implemented for Windows");

            var notifier = WindowsShimLoader.NewNativeNotifier();
            Assert.IsNotNull(notifier, "The shim should produce a notifier instance on Windows");

            var clicked = false;
            notifier.NotificationClicked = () => clicked = true;
            Assert.IsNotNull(notifier.NotificationClicked, "The click callback must be storable");

            notifier.NotificationClicked.Invoke();
            Assert.IsTrue(clicked, "The stored callback must be invocable");
        }

        /// <summary>
        /// The Linux notifier decides what an ActionInvoked signal means. The decision is
        /// reachable without a session bus; the notifier itself is not, because its
        /// constructor takes the session bus connection.
        /// </summary>
        [Test]
        public void ActionInvokedRaisesTheClickOnlyForTheDefaultAction()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("The D-Bus notifier is only supported on Linux");
                return;
            }

            var clicked = 0;
            void OnClicked() => clicked++;

            // The body of the notification was clicked.
            LinuxDBusNotifier.HandleActionInvoked(null, (1u, "default"), OnClicked);
            Assert.AreEqual(1, clicked, "Clicking the notification body should raise the callback");

            // One of the notification's own action buttons was used instead.
            LinuxDBusNotifier.HandleActionInvoked(null, (1u, "open-log"), OnClicked);
            Assert.AreEqual(1, clicked, "Another action must not be reported as a click on the notification");

            // The subscription itself failed; there is no action to report.
            LinuxDBusNotifier.HandleActionInvoked(new InvalidOperationException("bus went away"), default, OnClicked);
            Assert.AreEqual(1, clicked, "A failed subscription must not be reported as a click");
        }

        /// <summary>
        /// A notifier that was never given a callback must not throw when a signal arrives.
        /// </summary>
        [Test]
        public void ActionInvokedWithoutACallbackIsHarmless()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("The D-Bus notifier is only supported on Linux");
                return;
            }

            // Called directly rather than through Assert.DoesNotThrow: the platform guard
            // above does not reach inside a lambda, and the test fails on a throw either way.
            LinuxDBusNotifier.HandleActionInvoked(null, (1u, "default"), null);
        }
    }
}
