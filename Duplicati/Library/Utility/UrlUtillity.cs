#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
    public static class UrlUtillity
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
        public static void OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    throw new Exception("Malformed URL");

                if (string.IsNullOrEmpty(SystemBrowser))
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
                    process.StartInfo.FileName = SystemBrowser;
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
