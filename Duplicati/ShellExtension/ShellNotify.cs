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

using System.Runtime.InteropServices;

namespace Duplicati.ShellExtension;

/// <summary>
/// Asks Explorer to redraw items whose overlay may have changed
/// </summary>
internal static class ShellNotify
{
    /// <summary>
    /// An existing item has changed
    /// </summary>
    private const int SHCNE_UPDATEITEM = 0x00002000;
    /// <summary>
    /// The contents of an existing folder have changed
    /// </summary>
    private const int SHCNE_UPDATEDIR = 0x00001000;
    /// <summary>
    /// The item is a unicode path string
    /// </summary>
    private const uint SHCNF_PATHW = 0x0005;
    /// <summary>
    /// Return without waiting for the notification to be delivered
    /// </summary>
    private const uint SHCNF_FLUSHNOWAIT = 0x2000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, [MarshalAs(UnmanagedType.LPWStr)] string dwItem1, IntPtr dwItem2);

    /// <summary>
    /// Notifies Explorer that the folder and its contents should be redrawn
    /// </summary>
    /// <param name="path">The full path to the folder</param>
    public static void UpdateFolder(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // A bare drive letter is a drive-relative path; the root needs the separator
        if (path.Length == 2 && path[1] == ':')
            path += Path.DirectorySeparatorChar;

        try
        {
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
        }
        catch
        {
            // Redraw is best-effort; the next view refresh picks up the new status
        }
    }
}
