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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Some destinations list a file that was just uploaded only a few seconds later (#7665).
/// A file that was uploaded but not yet seen in a listing is listed again before it is reported
/// as missing; a file that stays missing, or one that was seen before, is still an error.
/// </summary>
public class UploadedFileMissingFromListingTests : BasicSetupHelper
{
    private string Target => new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;

    private Dictionary<string, string> Options(int retries) => new(TestOptions)
    {
        ["number-of-retries"] = retries.ToString(),
        ["retry-delay"] = "0s",
        ["no-auto-compact"] = "true",
    };

    [SetUp]
    public void AddBackend()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
    }

    [TearDown]
    public void ResetBackend()
    {
        DeterministicErrorBackend.ErrorGenerator = null;
        DeterministicErrorBackend.ListingFilter = null;
    }

    /// <summary>
    /// Leaves the dlist of the backup out of the given number of listings after it is uploaded.
    /// </summary>
    /// <returns>A function that tells how many listings left the dlist out</returns>
    private static Func<int> HideTheUploadedDlist(int listings)
    {
        string? dlist = null;
        var hidden = 0;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (action == DeterministicErrorBackend.BackendAction.PutAfter && remotename.Contains(".dlist."))
                dlist = remotename;
            return false;
        };
        DeterministicErrorBackend.ListingFilter = name =>
        {
            if (name != dlist || hidden >= listings)
                return false;
            hidden++;
            return true;
        };

        return () => hidden;
    }

    [Test]
    public async Task ADlistListedOnlyLaterDoesNotFailTheBackupAsync()
    {
        var hidden = HideTheUploadedDlist(1);

        using (var c = new Library.Main.Controller(Target, Options(2), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Assert.That(hidden(), Is.EqualTo(1), "The final listing left the dlist out once");

        DeterministicErrorBackend.ListingFilter = null;
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }

    [Test]
    [TestCase(0, 1)]
    [TestCase(2, 3)]
    public async Task ADlistThatStaysMissingFailsAfterTheRetriesAsync(int retries, int expectedListings)
    {
        var hidden = HideTheUploadedDlist(int.MaxValue);

        using (var c = new Library.Main.Controller(Target, Options(retries), null))
        {
            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await c.BackupAsync([DATAFOLDER]));
            Assert.That(error!.HelpID, Is.EqualTo("MissingRemoteFiles"));
        }

        Assert.That(hidden(), Is.EqualTo(expectedListings), "The listing is tried once more for each retry");
    }

    [Test]
    public async Task AFileSeenInAnEarlierListingIsNotListedAgainAsync()
    {
        using (var c = new Library.Main.Controller(Target, Options(2), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var dblock = Path.GetFileName(Directory.GetFiles(TARGETFOLDER, "*.dblock.*")[0]);
        var hidden = 0;
        DeterministicErrorBackend.ListingFilter = name =>
        {
            if (name != dblock)
                return false;
            hidden++;
            return true;
        };

        using (var c = new Library.Main.Controller(Target, Options(2), null))
        {
            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await c.TestAsync(1));
            Assert.That(error!.HelpID, Is.EqualTo("MissingRemoteFiles"));
        }

        Assert.That(hidden, Is.EqualTo(1), "A verified file that is missing is reported at once");
    }
}
