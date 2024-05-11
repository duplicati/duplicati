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
namespace Duplicati.Library.Common
{
    public static class Platform
    {
        /// <value>
        /// Gets or sets a value indicating if the client is Linux/Unix based
        /// </value>
        public static readonly bool IsClientPosix;


        /// <summary>
        /// Gets a value indicating if the client is Windows based
        /// </summary>
        public static readonly bool IsClientWindows;


        /// <value>
        /// Gets or sets a value indicating if the client is running OSX
        /// </value>
        public static readonly bool IsClientOSX;

        static Platform()
        {
            IsClientPosix = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
            IsClientWindows = !IsClientPosix;
            IsClientOSX = IsClientPosix && "Darwin".Equals(_RetrieveUname(false));
        }

        /// <value>
        /// Gets the output of "uname -a" on Linux, or null on Windows
        /// </value>
        public static string UnameAll
        {
            get
            {
                if (!IsClientPosix)
                    return null;

                return _RetrieveUname(true);

            }
        }

        private static string _RetrieveUname(bool showAll)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("uname", showAll ? "-a" : null)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);
                if (pi.HasExited)
                    return pi.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                return null;
            }

            return null;
        }

    }
}
