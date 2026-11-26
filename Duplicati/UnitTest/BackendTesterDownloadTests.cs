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
using System.IO;
using System.Security.Cryptography;
using Duplicati.CommandLine.BackendTester;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests how the backend tester checks a downloaded file. Before the real files are uploaded,
    /// a dummy file is uploaded under the names of the first and the last one, so a backend that
    /// serves an overwritten file late returns the dummy file for a while.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterDownload")]
    public class BackendTesterDownloadTests
    {
        private static readonly byte[] ExpectedContent = [1, 2, 3, 4];
        private static readonly byte[] DummyContent = [5, 6, 7, 8];
        private static readonly byte[] OtherContent = [9, 9, 9, 9];

        private string m_file;
        private SHA256 m_sha;

        [SetUp]
        public void SetUp()
        {
            m_file = Path.GetTempFileName();
            m_sha = SHA256.Create();
        }

        [TearDown]
        public void TearDown()
        {
            m_sha.Dispose();
            File.Delete(m_file);
        }

        private byte[] Hash(byte[] content) => m_sha.ComputeHash(content);

        private Program.DownloadedContent Compare(byte[] content, bool withDummyFile = true)
        {
            File.WriteAllBytes(m_file, content);
            return Program.CompareDownloadedFile(m_file, m_sha, Hash(ExpectedContent), withDummyFile ? Hash(DummyContent) : null);
        }

        /// <summary>
        /// Runs the download loop with a download that writes the given contents in turn
        /// </summary>
        private (Program.DownloadedContent Content, int Downloads) Download(int verificationRetries, Exception error, params byte[][] contents)
        {
            var downloads = 0;
            var content = Program.DownloadUntilNotDummyFile(() =>
            {
                File.WriteAllBytes(m_file, contents[Math.Min(downloads, contents.Length - 1)]);
                downloads++;
                return error;
            }, m_file, m_sha, Hash(ExpectedContent), Hash(DummyContent), verificationRetries);

            return (content, downloads);
        }

        [Test]
        public void TheExpectedFileMatches()
        {
            Assert.AreEqual(Program.DownloadedContent.Expected, Compare(ExpectedContent));
        }

        [Test]
        public void TheDummyFileIsRecognised()
        {
            Assert.AreEqual(Program.DownloadedContent.DummyFile, Compare(DummyContent),
                "A file with the dummy file's hash must be reported as the dummy file, not as corrupt.");
        }

        [Test]
        public void AFileThatIsNeitherIsCorrupt()
        {
            Assert.AreEqual(Program.DownloadedContent.Corrupt, Compare(OtherContent));
        }

        [Test]
        public void WithoutADummyFileAMismatchIsCorrupt()
        {
            Assert.AreEqual(Program.DownloadedContent.Corrupt, Compare(DummyContent, withDummyFile: false));
        }

        [Test]
        public void TheDummyFileIsDownloadedAgainUntilTheExpectedFileArrives()
        {
            var (content, downloads) = Download(3, null, DummyContent, ExpectedContent);

            Assert.AreEqual(Program.DownloadedContent.Expected, content);
            Assert.AreEqual(2, downloads);
        }

        [Test]
        public void TheDummyFileIsReportedWhenTheAttemptsRunOut()
        {
            var (content, downloads) = Download(1, null, DummyContent);

            Assert.AreEqual(Program.DownloadedContent.DummyFile, content);
            Assert.AreEqual(2, downloads, "One download and one more attempt.");
        }

        [Test]
        public void AnExpectedFileIsNotDownloadedAgain()
        {
            var (content, downloads) = Download(3, null, ExpectedContent);

            Assert.AreEqual(Program.DownloadedContent.Expected, content);
            Assert.AreEqual(1, downloads);
        }

        [Test]
        public void ACorruptFileIsNotDownloadedAgain()
        {
            var (content, downloads) = Download(3, null, OtherContent, ExpectedContent);

            Assert.AreEqual(Program.DownloadedContent.Corrupt, content,
                "Only the late arrival of an overwrite is waited for; other content is a real problem.");
            Assert.AreEqual(1, downloads);
        }

        [Test]
        public void AFailedDownloadIsNotRepeated()
        {
            var (_, downloads) = Download(3, new IOException("download failed"), DummyContent, ExpectedContent);

            Assert.AreEqual(1, downloads, "A download that failed was already retried by the tester.");
        }
    }
}
