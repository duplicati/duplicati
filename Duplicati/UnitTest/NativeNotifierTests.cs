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

        [Test]
        [SupportedOSPlatform("macOS")]
        public void MacOSOSANotifierCanBeCreated()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("The macOS notifier can only be created on macOS");

            var notifier = new Library.Utility.MacOSOSANotifier();
            Assert.IsNotNull(notifier, "A notifier instance should be created on macOS");

            var clicked = false;
            notifier.NotificationClicked = () => clicked = true;
            Assert.IsNotNull(notifier.NotificationClicked, "The click callback must be storable");

            notifier.NotificationClicked.Invoke();
            Assert.IsTrue(clicked, "The stored callback must be invocable");
        }
    }
}
