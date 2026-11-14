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

using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests how a \\?\Volume{GUID}\ source is turned into a regular path (#3264)
    /// </summary>
    public class VolumeMountPathTests
    {
        [Test]
        [Category("Utility")]
        [TestCase("D:", @"D:\", ExpectedResult = "D:")]
        [TestCase("D:", @"\\?\Volume{76ccfb3c-0000-0000-0000-010000000000}\", ExpectedResult = "D:")]
        [TestCase(null, @"C:\mnt\disk\", ExpectedResult = @"C:\mnt\disk\")]
        [TestCase("", @"C:\mnt\disk\", ExpectedResult = @"C:\mnt\disk\")]
        [TestCase(null, @"\\?\Volume{76ccfb3c-0000-0000-0000-010000000000}\", ExpectedResult = null)]
        [TestCase(null, null, ExpectedResult = null)]
        [TestCase(null, "", ExpectedResult = null)]
        public string SelectVolumeMountPathPrefersTheDriveLetterThenAFolder(string driveLetter, string name)
            => Utility.SelectVolumeMountPath(driveLetter, name);

        [Test]
        [Category("Utility")]
        [TestCase("D:", @"\data\", ExpectedResult = @"D:\data\")]
        [TestCase("D:", @"\data\file.txt", ExpectedResult = @"D:\data\file.txt")]
        [TestCase("D:", @"\", ExpectedResult = @"D:\")]
        [TestCase("D:", "", ExpectedResult = "D:")]
        [TestCase(@"C:\mnt\disk\", @"\data\", ExpectedResult = @"C:\mnt\disk\data\")]
        [TestCase(@"C:\mnt\disk\", @"\", ExpectedResult = @"C:\mnt\disk\")]
        [TestCase(@"C:\mnt\disk\", "", ExpectedResult = @"C:\mnt\disk\")]
        [TestCase(@"C:\mnt\disk", "data", ExpectedResult = @"C:\mnt\disk\data")]
        public string CombineVolumeMountPathJoinsTheMountAndThePathInTheVolume(string mountPath, string pathInVolume)
            => Utility.CombineVolumeMountPath(mountPath, pathInVolume);
    }
}
