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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A dlist name is only a time, so a new dlist file is given the first free second from a
/// start time, trying at most a given number of names.
/// </summary>
public class ProbeUnusedFilenameTests
{
    private static readonly DateTime Start = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>
    /// Registers dlist names for the given number of seconds from <see cref="Start"/>, and probes from it.
    /// </summary>
    private static async Task<DateTime> ProbeWithTakenAsync(int taken, int maxTries)
    {
        using var dbfile = new TempFile();
        await using var db = await LocalDatabase.CreateLocalDatabaseAsync(dbfile, "Test", true, null, CancellationToken.None);
        var options = new Options(new Dictionary<string, string?>());
        for (var i = 0; i < taken; i++)
            await db.RegisterRemoteVolumeAsync(VolumeBase.GenerateFilename(RemoteVolumeType.Files, options, null, Start.AddSeconds(i)), RemoteVolumeType.Files, RemoteVolumeState.Verified, CancellationToken.None);

        return await FilesetVolumeWriter.ProbeUnusedFilenameNameAsync(db, options, Start, CancellationToken.None, maxTries: maxTries);
    }

    [Test]
    [Category("Targeted")]
    [TestCase(0)]
    [TestCase(59)]
    public async Task ReturnsTheFirstFreeNameWithinTheTriesAsync(int taken)
        => Assert.That(await ProbeWithTakenAsync(taken, 60), Is.EqualTo(Start.AddSeconds(taken)));

    [Test]
    [Category("Targeted")]
    public void FailsWhenEveryNameWithinTheTriesIsTaken()
    {
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await ProbeWithTakenAsync(60, 60));
        Assert.That(ex!.HelpID, Is.EqualTo("FailedToFindUnusedFilename"));
    }
}
