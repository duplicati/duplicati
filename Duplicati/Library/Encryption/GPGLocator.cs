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
using System.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Encryption
{
    public static class GPGLocator
    {
        public static IEnumerable<string> GetGpgExecutableNames()
            => [
                OperatingSystem.IsWindows() ? "gpg2.exe" : "gpg2",
                OperatingSystem.IsWindows() ? "gpg.exe" : "gpg",
            ];

        public static string GetGpgExecutablePath()
        {
            var searchPaths = new List<string>();
            searchPaths.AddRange(Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? []);

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // return gpg4win if it exists
                    var location64 = RegistryUtility.GetDataByValueName(@"SOFTWARE\GnuPG", "Install Directory");
                    if (!string.IsNullOrEmpty(location64))
                        searchPaths.Add(Path.Combine(location64, "bin"));

                    var location32 = RegistryUtility.GetDataByValueName(@"SOFTWARE\WOW6432Node\GnuPG", "Install Directory");
                    if (!string.IsNullOrEmpty(location32))
                        searchPaths.Add(Path.Combine(location32, "bin"));
                }
            }
            catch
            {
            }

            var exeNames = new List<string>() {
                OperatingSystem.IsWindows() ? "gpg2.exe" : "gpg2",
                OperatingSystem.IsWindows() ? "gpg.exe" : "gpg",
            };

            foreach (var p in searchPaths)
            {
                foreach (var e in exeNames)
                {
                    var path = Path.Combine(p, e);
                    if (File.Exists(path))
                        return path;
                }
            }

            return OperatingSystem.IsWindows() ? null : "gpg";
        }
    }
}
