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

using Duplicati.Server.Serialization;

namespace Duplicati.GUI.TrayIcon
{
    /// <summary>
    /// Builds the text shown when the cursor rests on the tray icon
    /// </summary>
    public static class TrayTooltip
    {
        /// <summary>
        /// The longest text to show; Windows cuts a tray tooltip at 127 characters
        /// </summary>
        public const int MaxLength = 127;

        /// <summary>
        /// The text to show with a backup name when it is too long
        /// </summary>
        private const string Ellipsis = "...";

        /// <summary>
        /// Builds the tooltip for a server state
        /// </summary>
        /// <param name="appName">The application name</param>
        /// <param name="status">The server state</param>
        /// <returns>The tooltip, never <c>null</c></returns>
        public static string Build(string appName, IServerStatus status)
        {
            if (status.SuggestedStatusIcon == SuggestedStatusIcon.Disconnected)
                return Limit($"{appName}: not connected");

            var paused = status.ProgramState == LiveControlState.Paused;
            if (status.HasActiveTask)
            {
                var prefix = paused ? $"{appName}: paused while running " : $"{appName}: running ";
                if (string.IsNullOrWhiteSpace(status.ActiveBackupName))
                    return Limit(prefix + "a task");

                var name = status.ActiveBackupName.Trim();
                var room = MaxLength - prefix.Length - 2;
                if (name.Length > room)
                    name = name.Substring(0, System.Math.Max(0, room - Ellipsis.Length)) + Ellipsis;
                return Limit($"{prefix}\"{name}\"");
            }

            if (paused)
                return Limit($"{appName}: paused");

            return status.SuggestedStatusIcon switch
            {
                SuggestedStatusIcon.ReadyError => Limit($"{appName}: the last run had errors"),
                SuggestedStatusIcon.ReadyWarning => Limit($"{appName}: the last run had warnings"),
                _ => Limit(appName)
            };
        }

        /// <summary>
        /// Cuts a text to the longest a tooltip can show
        /// </summary>
        private static string Limit(string text)
            => text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
    }
}
