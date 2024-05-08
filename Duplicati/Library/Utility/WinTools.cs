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

using System.Runtime.Versioning;
using Duplicati.Library.Common;

namespace Duplicati.Library.Utility
{
    [SupportedOSPlatform("windows")]
    public static class WinTools
    {
        public static string GetWindowsGpgExePath()
        {
            if (!Platform.IsClientWindows)
            {
                return null;
            }

            try
            {
                // return gpg4win if it exists
                var location32 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\WOW6432Node\GnuPG", "Install Directory");
                var location64 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\GnuPG", "Install Directory");
                var gpg4winLocation = string.IsNullOrEmpty(location64) ? location32 : location64;

                if (!string.IsNullOrEmpty(gpg4winLocation))
                {
                    return System.IO.Path.Combine(gpg4winLocation, "bin", "gpg.exe");
                }
            }
            catch
            {
                // NOOP
            }

            // otherwise return our included win-tools
            var wintoolsPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "win-tools", "gpg.exe");
            return string.IsNullOrEmpty(wintoolsPath) ? null : wintoolsPath;
        }
    }
}
