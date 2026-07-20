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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.CommandLine.BackendTester;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the comparison of the backend's file listing against the files the backend
    /// tester uploaded. Every problem reported here makes the tester exit non-zero, which
    /// is what the live backend tests assert on, so a mistake in this comparison either
    /// hides a broken backend or fails a working one.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterVerification")]
    public class BackendTesterVerificationTests
    {
        /// <summary>
        /// Builds a listing entry; only the name, the size and the folder flag take part
        /// in the comparison.
        /// </summary>
        private static IFileEntry Entry(string name, long size, bool isFolder = false)
            => new Duplicati.Library.Common.IO.FileEntry(name, size, isFolder: isFolder);

        private static readonly (string Name, long Length)[] TwoFiles =
        [
            ("file-a", 100),
            ("file-b", 200)
        ];

        [Test]
        public void MatchingListingReportsNoProblems()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("file-b", 200)],
                TwoFiles, null, null);

            Assert.IsEmpty(problems, "A listing that matches the uploaded files must not report anything.");
        }

        [Test]
        public void FoldersAreIgnored()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("file-b", 200), Entry("a-subfolder", 0, isFolder: true)],
                TwoFiles, null, null);

            Assert.IsEmpty(problems, "A folder in the listing is not an unexpected file.");
        }

        [Test]
        public void DuplicateEntryIsReported()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("file-a", 100), Entry("file-b", 200)],
                TwoFiles, null, null);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("was found more than once", problems[0]);
        }

        [Test]
        public void SizeMismatchIsReported()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 999), Entry("file-b", 200)],
                TwoFiles, null, null);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("has size 100 but the size was reported as 999", problems[0]);
        }

        /// <summary>
        /// Backends that do not report a size list everything as zero. Comparing against
        /// that would report every single file as a size mismatch.
        /// </summary>
        [Test]
        public void UnreportedSizeIsNotAMismatch()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 0), Entry("file-b", 0)],
                TwoFiles, null, null);

            Assert.IsEmpty(problems, "A size of zero means the backend does not report sizes.");
        }

        [Test]
        public void MissingFileIsReported()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 100)],
                TwoFiles, null, null);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("was uploaded but not found afterwards", problems[0]);
        }

        [Test]
        public void UnexpectedFileIsReported()
        {
            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("file-b", 200), Entry("a-stranger", 50)],
                TwoFiles, null, null);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("a-stranger was found on server but not uploaded", problems[0]);
        }

        /// <summary>
        /// After a rename the old name must be gone and the new name must be present. A
        /// backend that copies instead of renaming, or that does nothing at all, is caught
        /// here; this is the condition behind #4126.
        /// </summary>
        [Test]
        public void RenameThatDidNotTakeEffectIsReported()
        {
            var expected = new[] { ("file-a", 100L), ("renamed-b", 200L) };

            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("file-b", 200)],
                expected, "file-b", "renamed-b");

            Assert.AreEqual(2, problems.Count, "Both the lingering old name and the missing new name are problems.");
            Assert.IsTrue(problems.Any(x => x.Contains("was supposed to have been renamed to renamed-b")),
                $"Expected the lingering old name to be reported, got: {string.Join(" | ", problems)}");
            Assert.IsTrue(problems.Any(x => x.Contains("renamed-b was uploaded but not found afterwards")),
                $"Expected the missing new name to be reported, got: {string.Join(" | ", problems)}");
        }

        [Test]
        public void CompletedRenameReportsNoProblems()
        {
            var expected = new[] { ("file-a", 100L), ("renamed-b", 200L) };

            var problems = Program.VerifyFileList(
                [Entry("file-a", 100), Entry("renamed-b", 200)],
                expected, "file-b", "renamed-b");

            Assert.IsEmpty(problems, "A rename that took effect must not report anything.");
        }
    }
}
