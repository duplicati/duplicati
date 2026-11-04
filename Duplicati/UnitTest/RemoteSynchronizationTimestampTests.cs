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
using Duplicati.Library.Main.Operation.RemoteSynchronization;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The remote synchronization planner copies a file again when the source copy is more recent than
/// the destination copy. The backends report the timestamps in different kinds: SMB in UTC, the
/// file backend in local time, and a plain parse of a WebDAV date in local time. Compared as they
/// are, a UTC source against a local-time destination is off by the zone offset, so on a machine
/// west of UTC every file was deleted and uploaded again on every run.
/// </summary>
[TestFixture]
public class RemoteSynchronizationTimestampTests
{
    /// <summary>
    /// A point in time far from any daylight saving transition, so the local-time form is unambiguous
    /// </summary>
    private static readonly DateTime Instant = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The same instant 44 seconds later, as the destination copy is written just after the source
    /// </summary>
    private static readonly DateTime Later = Instant.AddSeconds(44);

    [Test]
    public void AUtcSourceIsNotNewerThanALocalTimeDestinationWrittenAfterIt()
        => Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant, Later.ToLocalTime()), Is.False);

    [Test]
    public void ALocalTimeSourceIsNotNewerThanAUtcDestinationWrittenAfterIt()
        => Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant.ToLocalTime(), Later), Is.False);

    [Test]
    public void AUtcSourceWrittenAfterALocalTimeDestinationIsNewer()
        => Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Later, Instant.ToLocalTime()), Is.True);

    [Test]
    public void ALocalTimeSourceWrittenAfterAUtcDestinationIsNewer()
        => Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Later.ToLocalTime(), Instant), Is.True);

    [Test]
    public void TwoLocalTimesCompareAsBefore()
    {
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant.ToLocalTime(), Later.ToLocalTime()), Is.False);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Later.ToLocalTime(), Instant.ToLocalTime()), Is.True);
    }

    [Test]
    public void TwoUtcTimesCompareAsBefore()
    {
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant, Later), Is.False);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Later, Instant), Is.True);
    }

    [Test]
    public void AnUnspecifiedKindIsTakenAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(Instant, DateTimeKind.Unspecified);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(unspecified, Later), Is.False);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Later, unspecified), Is.True);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(unspecified, Later.ToLocalTime()), Is.False);
    }

    [Test]
    public void TheSameInstantIsNotNewerInAnyKind()
    {
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant, Instant.ToLocalTime()), Is.False);
        Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant.ToLocalTime(), Instant), Is.False);
    }

    [Test]
    public void AMissingDestinationTimeMakesTheSourceNewer()
        => Assert.That(RemoteSynchronizationRunner.IsSourceNewer(Instant, default), Is.True);
}
