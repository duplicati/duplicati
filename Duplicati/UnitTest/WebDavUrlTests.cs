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

using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The path names the folder at the destination, so what it decodes to has to be the folder
    /// the user asked for. Reported as issue #4880, where a "+" in the path was read as a space.
    /// </summary>
    [TestFixture]
    public class WebDavUrlTests
    {
        // A plus is a character here. Only a query string spells a space that way.
        [TestCase("My+Folder", "/My+Folder/")]
        [TestCase("a+b/c+d", "/a+b/c+d/")]
        // An encoded plus is the same character
        [TestCase("My%2BFolder", "/My+Folder/")]
        // A space is spelled the way a path spells one
        [TestCase("My%20Folder", "/My Folder/")]
        // The rest of the decoding is unchanged
        [TestCase("M%C3%A4ppe", "/Mäppe/")]
        [TestCase("Backup/Sub", "/Backup/Sub/")]
        // Already rooted and already ending in a separator
        [TestCase("/Backup/", "/Backup/")]
        public void ThePathKeepsWhatTheUserAskedFor(string path, string expected)
            => Assert.AreEqual(expected, WEBDAV.NormalizePath(path), path);
    }
}
