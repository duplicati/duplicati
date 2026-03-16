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

using System;
using System.Runtime.InteropServices;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Factory class for creating the appropriate <see cref="IDiskImageHelper"/> implementation
    /// based on the host operating system.
    /// </summary>
    public static class DiskImageHelperFactory
    {
        /// <summary>
        /// Creates and returns the appropriate disk image helper for the current operating system.
        /// </summary>
        /// <returns>An <see cref="IDiskImageHelper"/> implementation for the current OS.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when the current OS is not supported.</exception>
        public static IDiskImageHelper Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsDiskImageHelper();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new LinuxDiskImageHelper();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new MacOSDiskImageHelper();
            }
            else
            {
                throw new PlatformNotSupportedException($"Disk image operations are not supported on this operating system: {RuntimeInformation.OSDescription}");
            }
        }

        /// <summary>
        /// Gets a value indicating whether disk image operations are supported on the current platform.
        /// </summary>
        /// <value><c>true</c> if disk image operations are supported; otherwise, <c>false</c>.</value>
        public static bool IsSupported
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            }
        }

        /// <summary>
        /// Gets the name of the current platform.
        /// </summary>
        /// <value>The platform name ("Windows", "Linux", "macOS", or "Unknown").</value>
        public static string CurrentPlatformName
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return "Windows";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return "Linux";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return "macOS";
                else
                    return "Unknown";
            }
        }
    }
}
