#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    public static class UrlUtility
    {
        /// <summary>
        /// The file path to the system browser selected
        /// </summary>
        public static string SystemBrowser = null;

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
            if (Utility.IsClientOSX)
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
            else if (Utility.IsClientLinux)
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
            else
            {
                OpenUrlWindows(url, browserprogram);
            }
        }

        /// <summary>
        /// Opens the given URL in a browser
        /// </summary>
        /// <param name="url">The url to open, must start with http:// or https://</param>
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
