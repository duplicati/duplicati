// Copyright (C) 2024, The Duplicati Team
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
using System.Runtime.Versioning;
namespace Duplicati.Library.Common.IO
{
    public static class SystemIO
    {

        /// <summary>
        /// A cached lookup for windows methods for dealing with long filenames
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static readonly ISystemIO IO_WIN;

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macOS")]
        public static readonly ISystemIO IO_SYS;

        public static readonly ISystemIO IO_OS;

        static SystemIO()
        {
            // TODO: These interfaces cannot be properly guarded by the supported platform attribute in this form.
            // They are used in static methods of USNJournal on all platforms.
            IO_WIN = new SystemIOWindows();
            IO_SYS = new SystemIOLinux();
            if (OperatingSystem.IsWindows())
            {
                IO_OS = IO_WIN;
            }
            else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                IO_OS = IO_SYS;
            }
        }
    }
}