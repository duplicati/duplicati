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
using System.Linq;
using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
[Category("CopyToTempDestination")]
public class CopyToTempDestinationTests
{
    private static Backup CreateJob()
        => new()
        {
            TargetURL = "file:///main",
            ConnectionStringID = 3,
            Settings =
            [
                new Setting { Name = "passphrase", Value = "job-secret", Filter = "" },
                new Setting { Name = "--auth-username", Value = "main-user", Filter = "" },
                new Setting { Name = "dblock-size", Value = "10mb", Filter = "" }
            ],
            AdditionalTargetURLs =
            [
                new TargetUrlEntry
                {
                    TargetUrlKey = "copy", TargetUrl = "s3://bucket/copy", ConnectionStringID = 7, Mode = "interval", Interval = "1h",
                    Options = new Dictionary<string, object>
                    {
                        ["auth-username"] = JsonDocument.Parse("\"copy-user\"").RootElement,
                        ["passphrase"] = "destination-passphrase",
                        ["mode"] = "counting"
                    }
                },
                new TargetUrlEntry { TargetUrlKey = "other", TargetUrl = "file:///other" }
            ]
        };

    [Test]
    public void TheMainDestinationIsUsedWithoutAKey()
        => Assert.That(CopyToTempDestination.Resolve(CreateJob(), null), Is.Null);

    [Test]
    public void AKeyIsMatchedExactly()
    {
        Assert.That(CopyToTempDestination.Resolve(CreateJob(), "copy")!.TargetUrl, Is.EqualTo("s3://bucket/copy"));
        foreach (var key in new[] { "Copy", "no-such-key", "primary" })
        {
            var ex = Assert.Throws<UserInformationException>(() => CopyToTempDestination.Resolve(CreateJob(), key));
            Assert.That(ex!.HelpID, Is.EqualTo("CopyToTempDestinationUnknown"), key);
        }
        var empty = Assert.Throws<UserInformationException>(() => CopyToTempDestination.Resolve(CreateJob(), " "));
        Assert.That(empty!.HelpID, Is.EqualTo("CopyToTempDestinationEmpty"));
    }

    [Test]
    public void TheCopyReadsTheDestinationWithTheSettingsOfTheJob()
    {
        var job = CreateJob();
        var target = CopyToTempDestination.Resolve(job, "copy")!;
        CopyToTempDestination.Apply(job, target);

        Assert.That(job.TargetURL, Is.EqualTo("s3://bucket/copy"));
        Assert.That(job.ConnectionStringID, Is.EqualTo(7));
        Assert.That(job.AdditionalTargetURLs, Is.Empty, "The copy should not write to secondary destinations");

        var settings = job.Settings.ToDictionary(x => x.Name, x => x.Value);
        Assert.That(settings["--auth-username"], Is.EqualTo("copy-user"), "The options of the destination should replace those of the main one");
        Assert.That(settings["passphrase"], Is.EqualTo("job-secret"), "The files are encrypted with the passphrase of the job");
        Assert.That(settings["dblock-size"], Is.EqualTo("10mb"));
        Assert.That(settings.Keys, Has.None.EqualTo("--passphrase").And.None.EqualTo("--mode"));
    }
}
