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

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

#nullable enable

namespace Duplicati.Library.Utility.Power
{
    /// <summary>
    /// A macOS power management assertion that keeps the system awake while it is held.
    /// This is the same mechanism the <c>caffeinate</c> tool uses, but made in-process:
    /// the assertion is owned by this process and the kernel drops it when the process
    /// exits, so a crash cannot leave the machine awake. Dispose the instance to release it.
    /// </summary>
    [SupportedOSPlatform("macOS")]
    public sealed class MacOSSleepAssertion : IDisposable
    {
        /// <summary>
        /// The kind of sleep to prevent, mirroring the <c>caffeinate</c> flags
        /// </summary>
        public enum AssertionType
        {
            /// <summary>
            /// Prevents the system from sleeping while on AC power, like <c>caffeinate -s</c>.
            /// The assertion is not honoured on battery power.
            /// </summary>
            PreventSystemSleep,

            /// <summary>
            /// Prevents the system from idle sleeping, also on battery, like <c>caffeinate -i</c>
            /// </summary>
            PreventUserIdleSystemSleep,

            /// <summary>
            /// Prevents the display from idle sleeping, like <c>caffeinate -d</c>
            /// </summary>
            PreventUserIdleDisplaySleep
        }

        /// <summary>
        /// The path to the IOKit framework
        /// </summary>
        private const string IOKIT = "/System/Library/Frameworks/IOKit.framework/IOKit";

        /// <summary>
        /// The path to the CoreFoundation framework
        /// </summary>
        private const string COREFOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        /// <summary>
        /// <c>kIOPMAssertionLevelOn</c>: the assertion is active
        /// </summary>
        private const uint kIOPMAssertionLevelOn = 255;

        /// <summary>
        /// <c>kIOReturnSuccess</c>
        /// </summary>
        private const int kIOReturnSuccess = 0;

        /// <summary>
        /// <c>kCFStringEncodingUTF8</c>
        /// </summary>
        private const uint kCFStringEncodingUTF8 = 0x08000100;

        [DllImport(IOKIT)]
        private static extern int IOPMAssertionCreateWithName(IntPtr assertionType, uint assertionLevel, IntPtr assertionName, out uint assertionId);

        [DllImport(IOKIT)]
        private static extern int IOPMAssertionRelease(uint assertionId);

        [DllImport(COREFOUNDATION)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

        [DllImport(COREFOUNDATION)]
        private static extern void CFRelease(IntPtr cf);

        /// <summary>
        /// The assertion id handed out by IOKit
        /// </summary>
        private readonly uint m_assertionId;

        /// <summary>
        /// A flag indicating if the assertion has been released
        /// </summary>
        private bool m_released;

        /// <summary>
        /// The kind of sleep this assertion prevents
        /// </summary>
        public AssertionType Type { get; }

        /// <summary>
        /// The name the assertion is registered under, as shown by <c>pmset -g assertions</c>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="MacOSSleepAssertion"/> class
        /// </summary>
        /// <param name="type">The kind of sleep to prevent</param>
        /// <param name="name">The name the assertion is registered under</param>
        /// <param name="assertionId">The assertion id handed out by IOKit</param>
        private MacOSSleepAssertion(AssertionType type, string name, uint assertionId)
        {
            Type = type;
            Name = name;
            m_assertionId = assertionId;
        }

        /// <summary>
        /// Creates and activates a sleep assertion for the current process
        /// </summary>
        /// <param name="type">The kind of sleep to prevent</param>
        /// <param name="name">The name to register the assertion under; shown in <c>pmset -g assertions</c> and Activity Monitor</param>
        /// <returns>The held assertion; dispose it to release</returns>
        /// <exception cref="InvalidOperationException">Thrown if IOKit refuses the assertion</exception>
        public static MacOSSleepAssertion Create(AssertionType type, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("The assertion name must not be empty", nameof(name));

            var typeString = IntPtr.Zero;
            var nameString = IntPtr.Zero;
            try
            {
                typeString = CFStringCreateWithCString(IntPtr.Zero, GetAssertionTypeString(type), kCFStringEncodingUTF8);
                nameString = CFStringCreateWithCString(IntPtr.Zero, name, kCFStringEncodingUTF8);
                if (typeString == IntPtr.Zero || nameString == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create the CoreFoundation strings for the sleep assertion");

                var result = IOPMAssertionCreateWithName(typeString, kIOPMAssertionLevelOn, nameString, out var assertionId);
                if (result != kIOReturnSuccess)
                    throw new InvalidOperationException($"IOPMAssertionCreateWithName failed with IOReturn code 0x{result:X8}");

                return new MacOSSleepAssertion(type, name, assertionId);
            }
            finally
            {
                if (nameString != IntPtr.Zero)
                    CFRelease(nameString);
                if (typeString != IntPtr.Zero)
                    CFRelease(typeString);
            }
        }

        /// <summary>
        /// Maps the assertion type to the IOKit assertion type string
        /// </summary>
        /// <param name="type">The assertion type</param>
        /// <returns>The IOKit assertion type string</returns>
        private static string GetAssertionTypeString(AssertionType type)
            => type switch
            {
                AssertionType.PreventSystemSleep => "PreventSystemSleep",
                AssertionType.PreventUserIdleSystemSleep => "PreventUserIdleSystemSleep",
                AssertionType.PreventUserIdleDisplaySleep => "PreventUserIdleDisplaySleep",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown assertion type")
            };

        /// <summary>
        /// Releases the assertion, allowing the system to sleep again.
        /// Safe to call more than once.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if IOKit refuses to release the assertion</exception>
        public void Dispose()
        {
            if (m_released)
                return;

            m_released = true;
            var result = IOPMAssertionRelease(m_assertionId);
            if (result != kIOReturnSuccess)
                throw new InvalidOperationException($"IOPMAssertionRelease failed with IOReturn code 0x{result:X8}");
        }
    }
}
