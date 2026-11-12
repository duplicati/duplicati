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

using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A path is stored as the folder it is in and its name. A backup keeps the paths of the system
    /// that made it, so a path is split the same way on any system: a Windows path on its
    /// backslashes and slashes, a Unix path on its slashes only (ngclient#449)
    /// </summary>
    [TestFixture]
    public class SplitIntoPrefixAndNameTests
    {
        [Test]
        [TestCase(@"C:\Users\someone\Documents\payload.txt", @"C:\Users\someone\Documents\", "payload.txt")]
        [TestCase(@"C:\Users\someone\Documents\", @"C:\Users\someone\", @"Documents\")]
        [TestCase(@"C:\", "", @"C:\")]
        [TestCase(@"\\server\share\folder\file.txt", @"\\server\share\folder\", "file.txt")]
        [TestCase("C:/Users/someone/file.txt", "C:/Users/someone/", "file.txt")]
        [TestCase("/home/someone/Documents/payload.txt", "/home/someone/Documents/", "payload.txt")]
        [TestCase("/home/someone/Documents/", "/home/someone/", "Documents/")]
        [TestCase(@"/home/someone/a\b.txt", "/home/someone/", @"a\b.txt")]
        [TestCase(@"/home/some\one/", "/home/", @"some\one/")]
        [TestCase("/", "", "/")]
        public void APathIsSplitTheSameWayOnEverySystem(string path, string prefix, string name)
        {
            var split = LocalDatabase.SplitIntoPrefixAndName(path);

            Assert.That(split.Key, Is.EqualTo(prefix));
            Assert.That(split.Value, Is.EqualTo(name));
        }
    }
}
