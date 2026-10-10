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
using System.Reflection;
using Duplicati.Library.Backend.Storj;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A Storj bucket with default retention settings (Object Lock) refuses an upload that has an
/// expiry time (issue #6706). What counts is the value uplink.NET hands to the native library:
/// it sends <see cref="DateTime.UnixEpoch"/> as zero, which is no expiry.
/// </summary>
[TestFixture]
public class StorjUploadOptionsTests
{
    /// <summary>
    /// Converts the options the way uplink.NET does before an upload, and returns the expiry time
    /// in seconds since 1970, where zero means no expiry
    /// </summary>
    private static long ExpiresSentToUplink(uplink.NET.Models.UploadOptions options)
    {
        var toSwig = typeof(uplink.NET.Models.UploadOptions).GetMethod("ToSWIG", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var swig = toSwig.Invoke(options, null)!;
        return (long)swig.GetType().GetProperty("expires")!.GetValue(swig)!;
    }

    [Test]
    public void AnUploadHasNoExpiryTime()
    {
        var options = Storj.CreateUploadOptions();
        Assert.That(options.Expires, Is.EqualTo(DateTime.UnixEpoch));

        // The value handed to the native library is read on Windows only. The native library
        // contains a Go runtime, and loading it into the test process on Linux made the process
        // crash later, in other tests.
        if (OperatingSystem.IsWindows())
            Assert.That(ExpiresSentToUplink(options), Is.EqualTo(0));
    }
}
