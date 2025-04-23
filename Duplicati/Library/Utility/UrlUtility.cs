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
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using Duplicati.Library.Common;

namespace Duplicati.Library.Utility
{
    public static class UrlUtility
    {
        /// <summary>
        /// The file path to the system browser selected
        /// </summary>
        public static readonly string SystemBrowser = null;

        /// <summary>
        /// A delegate for handing error messages
        /// </summary>
        /// <param name="errormessage">The message to display the error for</param>
        public delegate void ErrorHandlerDelegate(string errormessage);

        /// <summary>
        /// The errorhandler callback method
        /// </summary>
        public static ErrorHandlerDelegate ErrorHandler;

        /// <summary>
        /// Opens the given URL in a browser
        /// </summary>
        /// <param name="url">The url to open, must start with http:// or https://</param>
        public static void OpenURL(string url, string browserprogram = null)
        {
            if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
                throw new Exception("Malformed URL");

            if (string.IsNullOrWhiteSpace(browserprogram))
                browserprogram = SystemBrowser;

            //Fallback is to just show the window in a browser
            if (OperatingSystem.IsMacOS())
            {
                try
                {
                    var cmd = string.IsNullOrWhiteSpace(browserprogram) ? "open" : browserprogram;
                    System.Diagnostics.Process.Start(cmd, "\"" + url + "\"");
                }
                catch
                {
                    if (ErrorHandler != null)
                        ErrorHandler(string.Format("Unable to open a browser window, please manually visit: \r\n{0}", url));
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                try
                {
                    var apps = new string[] {browserprogram, "xdg-open", "chromium-browser", "google-chrome", "firefox", "mozilla", "konqueror", "netscape", "opera", "epiphany" };
                    foreach(var n in apps)
                        if (!string.IsNullOrWhiteSpace(n) && Duplicati.Library.Utility.Utility.Which(n))
                        {
                            System.Diagnostics.Process.Start(n, "\"" + url + "\"");
                            return;
                        }

                    if (ErrorHandler != null)
                        ErrorHandler("No suitable browser found, try installing \"xdg-open\"");

                    Console.WriteLine("No suitable browser found, try installing \"xdg-open\"");
                }
                catch
                {
                    if (ErrorHandler != null)
                        ErrorHandler(string.Format("Unable to open a browser window, please manually visit: \r\n{0}", url));
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                OpenUrlWindows(url, browserprogram);
            }
            else
            {
                throw new NotSupportedException("Unsupported Operating System");
            }
        }

        /// <summary>
        /// Opens the given URL in a browser
        /// </summary>
        /// <param name="url">The url to open, must start with http:// or https://</param>
        [SupportedOSPlatform("windows")]
        private static void OpenUrlWindows(string url, string browserprogram)
        {
            if (string.IsNullOrWhiteSpace(browserprogram))
                browserprogram = SystemBrowser;

            try
            {
                if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
                    throw new Exception("Malformed URL");

                if (string.IsNullOrEmpty(browserprogram))
                {
                    try
                    {
                        System.Diagnostics.Process process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = url;
                        process.StartInfo.UseShellExecute = true;
                        process.Start();
                    }
                    catch
                    {
                        //The straightforward method gives an error: "The requested lookup key was not found in any active activation context"
                        System.Diagnostics.Process process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = "rundll32.exe";
                        process.StartInfo.Arguments = "url.dll,FileProtocolHandler " + url;
                        process.StartInfo.UseShellExecute = true;
                        process.Start();
                    }
                }
                else
                {
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = browserprogram;
                    process.StartInfo.Arguments = url;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                }

            }
            catch
            {
                if (ErrorHandler != null)
                    ErrorHandler(string.Format("Unable to open a browser window, please manually visit: \r\n{0}", url));
            }

        }
    }
}
