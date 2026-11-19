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

using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
[Category("DeleteRemoteDestinations")]
public class RemoteDestinationDeleteTests
{
    private static Backup CreateJob(string primary, params (string Key, string Url)[] secondary)
        => new()
        {
            TargetURL = primary,
            AdditionalTargetURLs = secondary.Select(x => (ITargetUrlEntry)new TargetUrlEntry { TargetUrlKey = x.Key, TargetUrl = x.Url }).ToList()
        };

    [Test]
    public void LeavingOutTheParameterKeepsTheOlderBehaviour()
    {
        var job = CreateJob("file:///a", ("b", "file:///b"));
        Assert.That(RemoteDestinationDelete.Resolve(job, null, true), Is.Null);
        Assert.That(RemoteDestinationDelete.Resolve(job, null, null), Is.Null);
    }

    [Test]
    public void TheDestinationsAreDeletedInTheOrderOfTheJobOnce()
    {
        var job = CreateJob("file:///a", ("b", "file:///b"), ("c", "file:///c"));
        Assert.That(RemoteDestinationDelete.Resolve(job, ["c,primary", " c ", "b"], null), Is.EqualTo(new[] { "primary", "b", "c" }));
    }

    [Test]
    public void AKeyIsMatchedExactly()
    {
        var job = CreateJob("file:///a", ("Key", "file:///b"));
        var ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["key"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsUnknown"));
        ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["Primary"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsUnknown"));
    }

    [Test]
    public void ASecondaryDestinationNamedLikeTheMainOneIsRefused()
    {
        var job = CreateJob("file:///a", ("PRIMARY", "file:///b"));
        var ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["PRIMARY"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsAmbiguousKey"));
    }

    [Test]
    public void ADestinationAtThePlaceOfOneThatIsKeptIsRefused()
    {
        // The credentials, the options and the case do not make it another place
        var job = CreateJob("s3://bucket/folder?auth-username=a", ("b", "S3://user:pass@bucket/folder/?auth-username=b"), ("c", "s3://bucket/other"));
        var ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["b"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsSharedLocation"));
        ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["primary", "c"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsSharedLocation"));

        // Both are chosen, so nothing at that place is kept
        Assert.That(RemoteDestinationDelete.Resolve(job, ["primary", "b"], null), Is.EqualTo(new[] { "primary", "b" }));
    }

    [Test]
    public void ASyncJobRefusesDestinationsAndTheOlderParameterTogetherIsRefused()
    {
        var job = CreateJob("file:///a", ("b", "file:///b"));
        var ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["b"], false));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsConflict"));

        job.OperationType = Duplicati.Server.Serialization.OperationType.Sync;
        ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["b"], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsSyncJob"));

        ex = Assert.Throws<UserInformationException>(() => RemoteDestinationDelete.Resolve(job, ["", " , "], null));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteDestinationsEmpty"));
    }
}
