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
using System.Globalization;
using Duplicati.Library.Backend;
using Duplicati.Library.Backend.Storj;
using Duplicati.Library.Backend.TencentCOS;
using NUnit.Framework;
using uplink.NET.Models;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A backend reports the modification time of a file in UTC. A plain parse of the value a service
/// returns converts it to local time instead, and the remote synchronization planner then compares
/// it against the UTC timestamps of other backends, which west of UTC makes every destination file
/// look older than its source. These are the backends that parse a string value themselves.
/// </summary>
[TestFixture]
public class BackendTimestampTests
{
    /// <summary>
    /// The instant every value below denotes
    /// </summary>
    private static readonly DateTime Instant = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static void AssertIsInstantInUtc(DateTime actual)
    {
        Assert.That(actual.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(actual, Is.EqualTo(Instant));
    }

    /// <summary>
    /// rclone lsjson gives an RFC 3339 value, with nanoseconds and the zone of the remote
    /// </summary>
    [TestCase("2026-07-15T12:00:00Z")]
    [TestCase("2026-07-15T14:00:00+02:00")]
    [TestCase("2026-07-15T08:00:00-04:00")]
    [TestCase("2026-07-15T12:00:00.000000000Z")]
    public void RcloneReportsUtc(string modTime)
        => AssertIsInstantInUtc(Rclone.ParseModTime(modTime));

    /// <summary>
    /// The nanoseconds are rounded to the nearest tick
    /// </summary>
    [Test]
    public void RcloneKeepsTheFractionItCanRepresent()
        => Assert.That(Rclone.ParseModTime("2026-07-15T12:00:00.123456789Z"), Is.EqualTo(Instant.AddTicks(1234568)));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void RcloneReportsTheEpochForAMissingValue(string? modTime)
    {
        var parsed = Rclone.ParseModTime(modTime);
        Assert.That(parsed.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(parsed.Ticks, Is.EqualTo(0));
    }

    /// <summary>
    /// Tencent COS gives an ISO 8601 value in UTC with milliseconds
    /// </summary>
    [TestCase("2026-07-15T12:00:00.000Z")]
    [TestCase("2026-07-15T12:00:00Z")]
    [TestCase("2026-07-15T20:00:00.000+08:00")]
    public void TencentCosReportsUtc(string lastModified)
        => AssertIsInstantInUtc(COS.ParseLastModified(lastModified));

    /// <summary>
    /// pCloud gives an RFC 2822 value, documented as "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    [TestCase("Wed, 15 Jul 2026 12:00:00 +0000")]
    [TestCase("Wed, 15 Jul 2026 14:00:00 +0200")]
    [TestCase("Wed, 15 Jul 2026 08:00:00 -0400")]
    public void PCloudReportsUtc(string timestamp)
        => AssertIsInstantInUtc(pCloudBackend.ParseTimestamp(timestamp));

    /// <summary>
    /// Storj keeps the timestamps the backend itself wrote as custom metadata, in the round-trip
    /// format of a UTC value
    /// </summary>
    [Test]
    public void StorjReportsUtc()
    {
        var written = Instant.ToString("O", CultureInfo.InvariantCulture);
        var custom = new CustomMetadata();
        custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_ACCESS, Value = written });
        custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_MODIFICATION, Value = written });
        var file = new StorjFile(new uplink.NET.Models.Object
        {
            Key = "duplicati-20260101T000000Z.dlist.zip.aes",
            IsPrefix = false,
            CustomMetadata = custom,
            SystemMetadata = new SystemMetadata { ContentLength = 10 }
        });

        AssertIsInstantInUtc(file.LastModification);
        AssertIsInstantInUtc(file.LastAccess);
    }

    [Test]
    public void StorjReportsTheMinimumForAMissingValue()
    {
        var file = new StorjFile(new uplink.NET.Models.Object
        {
            Key = "duplicati-20260101T000000Z.dlist.zip.aes",
            IsPrefix = false,
            CustomMetadata = new CustomMetadata(),
            SystemMetadata = new SystemMetadata { ContentLength = 10 }
        });

        Assert.That(file.LastModification, Is.EqualTo(DateTime.MinValue));
        Assert.That(file.LastAccess, Is.EqualTo(DateTime.MinValue));
    }
}
