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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Four volumes written at once to a local folder, which is often an external hard disk, made
/// the backup slow there, so a local folder uploads one volume at a time unless told otherwise (#4808)
/// </summary>
public class LocalDestinationUploadLimitTests
{
    private static int LimitFor(string backendUrl, Dictionary<string, string?>? options = null)
        => new Options(options ?? []).GetAsynchronousConcurrentUploadLimit(backendUrl);

    [Test]
    [Category("Options")]
    [TestCase("file://C:\\backup")]
    [TestCase("file:///mnt/usb/backup")]
    [TestCase("C:\\backup")]
    [TestCase("/mnt/usb/backup")]
    public void ALocalFolderUploadsOneVolumeAtATime(string backendUrl)
        => Assert.That(LimitFor(backendUrl), Is.EqualTo(1));

    [Test]
    [Category("Options")]
    [TestCase("s3://bucket/prefix")]
    [TestCase("ftp://example.com/backup")]
    [TestCase("webdav://example.com/backup")]
    public void OtherDestinationsKeepTheDefault(string backendUrl)
        => Assert.That(LimitFor(backendUrl), Is.EqualTo(4));

    [Test]
    [Category("Options")]
    [TestCase("asynchronous-upload-limit")]
    [TestCase("asynchronous-concurrent-upload-limit")]
    public void AGivenLimitIsUsedForALocalFolder(string option)
        => Assert.That(LimitFor("file://C:\\backup", new() { [option] = "3" }), Is.EqualTo(3));

    [Test]
    [Category("Options")]
    public void AGivenLimitIsUsedForOtherDestinations()
        => Assert.That(LimitFor("s3://bucket/prefix", new() { ["asynchronous-upload-limit"] = "2" }), Is.EqualTo(2));
}
