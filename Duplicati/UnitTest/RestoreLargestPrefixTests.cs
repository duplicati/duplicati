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

using System.Collections.Generic;
using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the step that shortens a path when looking for the largest prefix the restored paths share
    /// </summary>
    [TestFixture]
    [Category("Restore")]
    public class RestoreLargestPrefixTests
    {
        /// <summary>
        /// Shortens a path one folder at a time until nothing is left, as the prefix search does
        /// when the paths share no folder
        /// </summary>
        private static List<string> ShortenToEmpty(string path, string dirsep)
        {
            var steps = new List<string>();
            while (path.Length > 0 && steps.Count < 100)
            {
                path = LocalRestoreDatabase.GetParentPrefix(path, dirsep);
                steps.Add(path);
            }

            return steps;
        }

        [Test]
        public void Unc_path_shortens_to_empty_without_failing()
        {
            // A restore to a folder of a UNC path together with a drive path shares no folder,
            // so the UNC path is shortened all the way down
            Assert.That(ShortenToEmpty(@"\\duplicati\hyperv\abc\", @"\"), Is.EqualTo(new[] {
                @"\\duplicati\hyperv\",
                @"\\duplicati\",
                @"\\",
                @"\",
                "",
            }));
        }

        [Test]
        public void Drive_path_shortens_to_empty()
        {
            Assert.That(ShortenToEmpty(@"C:\Users\Documents\", @"\"), Is.EqualTo(new[] {
                @"C:\Users\",
                @"C:\",
                "",
            }));
        }

        [Test]
        public void Unix_path_shortens_to_empty()
        {
            Assert.That(ShortenToEmpty("/home/user/", "/"), Is.EqualTo(new[] {
                "/home/",
                "/",
                "",
            }));
        }

        [Test]
        public void File_path_shortens_to_its_folder()
        {
            Assert.That(LocalRestoreDatabase.GetParentPrefix(@"C:\Data\file.txt", @"\"), Is.EqualTo(@"C:\Data\"));
        }
    }
}
