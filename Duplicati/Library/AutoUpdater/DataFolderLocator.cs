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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Common.IO;

#nullable enable

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Helper class to locate the default storage folder for the application
/// </summary>
public static class DataFolderLocator
{
    /// <summary>
    /// Finds a default storage folder, using the operating system specific locations.
    /// The targetfilename is used to detect locations that are used in previous versions.
    /// If the targetfilename is found in an old location, but not the current, the old location is used.
    /// Note that the folder is not created, only the path is returned.
    /// If the data folder is overriden, the overriden folder is used, and no search is performed.
    /// </summary>
    /// <param name="targetfilename">The filename to look for</param>
    /// <param name="autoCreate">Whether to create the folder if it does not exist</param>
    /// <returns>The default storage folder</returns>
    public static string GetDefaultStorageFolder(string targetfilename, bool autoCreate)
    {
        var folder = DataFolderManager.OVERRIDEN_DATAFOLDER
            ? DataFolderManager.DATAFOLDER
            : GetDefaultStorageFolderInternal(targetfilename, AutoUpdateSettings.AppName);

        if (SystemIO.IO_OS.DirectoryExists(folder))
        {
            if (!SystemIO.IO_OS.FileExists(System.IO.Path.Combine(folder, Util.InsecurePermissionsMarkerFile)))
                SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(folder);
        }
        else if (autoCreate)
        {
            // Create the folder
            SystemIO.IO_OS.DirectoryCreate(folder);

            // Make sure the folder is only accessible by the current user
            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(folder);
        }

        return folder;
    }

    /// <summary>
    /// Finds a default storage folder, using the operating system specific locations.
    /// The targetfilename is used to detect locations that are used in previous versions.
    /// If the targetfilename is found in an old location, but not the current, the old location is used.
    /// Note that the folder is not created, only the path is returned.
    /// </summary>
    /// <param name="targetfilename">The filename to look for</param>
    /// <param name="appName">The name of the application</param>
    /// <returns>The default storage folder</returns>
    internal static string GetDefaultStorageFolderInternal(string targetfilename, string appName)
    {
        //Normal mode uses the systems "(Local) Application Data" folder
        // %LOCALAPPDATA% on Windows, ~/.config on Linux
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);

        if (OperatingSystem.IsWindows())
        {
            // Special handling for Windows:
            //   - Older versions use %APPDATA%
            //   - New versions use %LOCALAPPDATA%
            //   - And prevent using C:\Windows\
            var newlocation = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);

            var folderOrder = new List<string>() {
                    folder,
                    newlocation
                };

            // If %LOCALAPPDATA% is inside the Windows folder, prefer a LocalService folder instead
            if (Common.IO.Util.IsPathUnderWindowsFolder(newlocation))
                folderOrder.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), appName));

            // Prefer the most recent location
            var matches = folderOrder.AsEnumerable()
                .Reverse()
                .Where(x => System.IO.File.Exists(System.IO.Path.Combine(x, targetfilename)))
                .ToList();

            // Use the most recent location found with content
            // If none are found, use the most recent location
            folder = matches.FirstOrDefault() ?? folderOrder.Last();
        }

        if (OperatingSystem.IsMacOS())
        {
            // Special handling for MacOS:
            //   - Older versions use ~/.config/
            //   - but new versions use ~/Library/Application\ Support/
            var configfolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", appName);

            var prevfile = System.IO.Path.Combine(configfolder, targetfilename);
            var curfile = System.IO.Path.Combine(folder, targetfilename);

            // If the old file exists, and not the new file, we use the old
            // Otherwise we use the new location
            if (!System.IO.File.Exists(curfile) && System.IO.File.Exists(prevfile))
                folder = configfolder;
        }

        if (OperatingSystem.IsLinux() && (folder == $"/{appName}" || folder == appName))
        {
            // Special handling for Linux with no home folder:
            //   - Older versions use /
            //   - but new versions use /var/lib/
            var libfolder = System.IO.Path.Combine("var", "lib", appName);

            var curfile = System.IO.Path.Combine(libfolder, targetfilename);
            var prevfile = System.IO.Path.Combine(folder, targetfilename);

            // If the old file exists, and not the new file, we use the old
            // Otherwise we use the new location
            if (System.IO.File.Exists(curfile) || !System.IO.File.Exists(prevfile))
                folder = libfolder;
        }

        return folder;
    }
}


