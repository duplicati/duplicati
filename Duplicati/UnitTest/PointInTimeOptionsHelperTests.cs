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
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The point-in-time option accepts durations counting back from now and absolute times.
/// </summary>
[TestFixture]
public class PointInTimeOptionsHelperTests
{
    private static Dictionary<string, string?> Options(string? value)
        => value == null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [PointInTimeOptionsHelper.PointInTimeOption] = value };

    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    [Test]
    [Category("Backend")]
    public void AbsentOptionGivesNull()
    {
        Assert.IsNull(PointInTimeOptionsHelper.Parse(Options(null)));
        Assert.IsNull(PointInTimeOptionsHelper.Parse(Options(string.Empty)));
        Assert.IsNull(PointInTimeOptionsHelper.Parse(Options("  ")));
    }

    [Test]
    [Category("Backend")]
    public void DurationCountsBackFromNow()
    {
        var expected = DateTime.UtcNow.AddDays(-7);

        var parsed = PointInTimeOptionsHelper.Parse(Options("7D"));

        Assert.IsNotNull(parsed);
        Assert.AreEqual(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Less((parsed.Value - expected).Duration(), Tolerance);
    }

    [Test]
    [Category("Backend")]
    public void AbsoluteLocalTimeIsConvertedToUtc()
    {
        var local = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Local);

        var parsed = PointInTimeOptionsHelper.Parse(Options(local.ToString("yyyy-MM-dd HH:mm:ss")));

        Assert.AreEqual(local.ToUniversalTime(), parsed);
        Assert.AreEqual(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Test]
    [Category("Backend")]
    public void SerializedUtcTimeIsUsedAsGiven()
    {
        var parsed = PointInTimeOptionsHelper.Parse(Options("20260115T083000Z"));

        Assert.AreEqual(new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc), parsed);
    }

    [Test]
    [Category("Backend")]
    public void FutureTimeIsClampedToNow()
    {
        var before = DateTime.UtcNow;

        var parsed = PointInTimeOptionsHelper.Parse(Options("2099-01-01"));

        Assert.IsNotNull(parsed);
        Assert.GreaterOrEqual(parsed!.Value, before);
        Assert.LessOrEqual(parsed.Value, DateTime.UtcNow);
    }

    [Test]
    [Category("Backend")]
    public void NowIsAccepted()
    {
        var parsed = PointInTimeOptionsHelper.Parse(Options("now"));

        Assert.IsNotNull(parsed);
        Assert.Less((DateTime.UtcNow - parsed!.Value).Duration(), Tolerance);
    }

    [Test]
    [Category("Backend")]
    public void InvalidValueIsReported()
    {
        var ex = Assert.Throws<UserInformationException>(() => PointInTimeOptionsHelper.Parse(Options("last tuesday")));

        Assert.AreEqual(PointInTimeOptionsHelper.HelpIdInvalidPointInTime, ex!.HelpID);
        Assert.That(ex.Message, Does.Contain("last tuesday"));
        Assert.That(ex.Message, Does.Contain(PointInTimeOptionsHelper.PointInTimeOption));
    }

    [Test]
    [Category("Backend")]
    public void ApplyToCopiesTheRawValue()
    {
        var target = new Dictionary<string, string?>();

        PointInTimeOptionsHelper.ApplyTo(Options("7D"), target);
        Assert.AreEqual("7D", target[PointInTimeOptionsHelper.PointInTimeOption]);

        var untouched = new Dictionary<string, string?>();
        PointInTimeOptionsHelper.ApplyTo(Options(null), untouched);
        Assert.IsEmpty(untouched);
    }

    [Test]
    [Category("Backend")]
    public void ReadOnlyExceptionNamesTheOption()
    {
        var ex = PointInTimeOptionsHelper.CreateReadOnlyException();

        Assert.AreEqual(PointInTimeOptionsHelper.HelpIdDestinationReadOnly, ex.HelpID);
        Assert.That(ex.Message, Does.Contain(PointInTimeOptionsHelper.PointInTimeOption));
    }
}
