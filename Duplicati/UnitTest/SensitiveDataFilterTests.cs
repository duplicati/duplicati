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

using NUnit.Framework;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SensitiveDataFilterTests
    {
        [Test]
        public void RedactPaths_UnixPaths()
        {
            var input = "Failed to access /home/user/secret.txt";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Failed to access -redacted-"));
        }

        [Test]
        public void RedactPaths_WindowsPaths()
        {
            var input = "Failed to access C:\\Users\\user\\secret.txt";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Failed to access -redacted-"));
        }

        [Test]
        public void RedactPaths_UNCPaths()
        {
            var input = "Failed to access \\\\server\\share\\folder\\file.txt";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Failed to access -redacted-"));
        }

        [Test]
        public void RedactPaths_FileUris()
        {
            var input = "Found file:///home/user/secret.txt";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Found -redacted-"));
        }

        [Test]
        public void RedactPaths_MultiplePaths()
        {
            var input = "Paths: /home/user/secret.txt and C:\\Users\\user\\other.txt";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Paths: -redacted- and -redacted-"));
        }

        [Test]
        public void RedactPaths_PathsInQuotes()
        {
            var input = "Path \"/home/user/secret.txt\" not found";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Path \"-redacted-\" not found"));
        }

        [Test]
        public void RedactPaths_PathsInParens()
        {
            var input = "File (/home/user/secret.txt) missing";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("File (-redacted-) missing"));
        }

        [Test]
        public void RedactPaths_StackTrace()
        {
            var input = "at Duplicati.Main.Run() in /Users/builder/project/Duplicati/Main.cs:line 42";
            var result = SensitiveDataFilter.RedactPaths(input);
            // The path is redacted; trailing stack trace metadata may also be consumed
            Assert.That(result, Does.Contain("-redacted-"));
            Assert.That(result, Does.Not.Contain("/Users/builder/project/Duplicati/Main.cs"));
        }

        [Test]
        public void RedactPaths_DoesNotRedactUrls()
        {
            var input = "Visit https://example.com/path/to/resource";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Visit https://example.com/path/to/resource"));
        }

        [Test]
        public void RedactPaths_DoesNotRedactDates()
        {
            var input = "Date: 2026/01/01";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Date: 2026/01/01"));
        }

        [Test]
        public void RedactPaths_DoesNotRedactDivision()
        {
            var input = "Result: 100 / 200";
            var result = SensitiveDataFilter.RedactPaths(input);
            Assert.That(result, Is.EqualTo("Result: 100 / 200"));
        }

        [Test]
        public void RedactPaths_EmptyInput()
        {
            Assert.That(SensitiveDataFilter.RedactPaths(null), Is.Null);
            Assert.That(SensitiveDataFilter.RedactPaths(""), Is.EqualTo(""));
        }

        [Test]
        public void RedactPathArguments_WindowsPathWithSpaces()
        {
            // The text-based detection stops at whitespace, so a path with spaces leaks
            // its tail; at the argument level the whole value is known to be one path.
            var args = new object[] { "C:\\System Volume Information\\6{3808876b-c176-4e48-b7ae-04046e6cc752}" };
            var result = SensitiveDataFilter.RedactPathArguments(args);
            Assert.That(result[0], Is.EqualTo("-redacted-"));

            var formatted = string.Format("Excluding path due to permission denied: {0}", result);
            Assert.That(formatted, Is.EqualTo("Excluding path due to permission denied: -redacted-"));
        }

        [Test]
        public void RedactPathArguments_RemoteNameWithTrailingSeparator()
        {
            var args = new object[] { " SOME FOLDER/" };
            var result = SensitiveDataFilter.RedactPathArguments(args);
            Assert.That(result[0], Is.EqualTo("-redacted-"));
        }

        [Test]
        public void RedactPathArguments_LeavesNonPathArgumentsAlone()
        {
            var args = new object[] { "duplicati-b1.dblock.zip", 42, null, "Backup_ProcessingFiles" };
            var result = SensitiveDataFilter.RedactPathArguments(args);
            Assert.That(result, Is.SameAs(args), "Arguments without paths are returned unchanged");
            Assert.That(args[0], Is.EqualTo("duplicati-b1.dblock.zip"));
        }

        [Test]
        public void RedactPathArguments_DoesNotModifyInput()
        {
            var args = new object[] { "/home/user/secret.txt", "other" };
            var result = SensitiveDataFilter.RedactPathArguments(args);
            Assert.That(result, Is.Not.SameAs(args));
            Assert.That(args[0], Is.EqualTo("/home/user/secret.txt"), "The input array must not be modified");
            Assert.That(result[0], Is.EqualTo("-redacted-"));
            Assert.That(result[1], Is.EqualTo("other"));
        }

        [Test]
        public void RedactPathArguments_EmptyInput()
        {
            Assert.That(SensitiveDataFilter.RedactPathArguments(null), Is.Null);
            Assert.That(SensitiveDataFilter.RedactPathArguments(new object[0]), Is.Empty);
        }

        [Test]
        public void LooksLikePath_Detection()
        {
            Assert.That(SensitiveDataFilter.LooksLikePath(null), Is.False);
            Assert.That(SensitiveDataFilter.LooksLikePath(""), Is.False);
            Assert.That(SensitiveDataFilter.LooksLikePath("file.txt"), Is.False);
            Assert.That(SensitiveDataFilter.LooksLikePath("folder/"), Is.True);
            Assert.That(SensitiveDataFilter.LooksLikePath("C:\\path with spaces\\file.txt"), Is.True);
            Assert.That(SensitiveDataFilter.LooksLikePath("/home/user"), Is.True);
        }
    }
}
