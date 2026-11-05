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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Backend;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The remote synchronization tool lists its destination before it copies to it. A destination
/// folder that disappears after that, as an unplugged drive does, must not be created again, or
/// the copies go to a new, empty folder in its place. A destination that is not there to begin
/// with is still created.
/// </summary>
public class LightWeightVanishingDestinationTests : BasicSetupHelper
{
    private static LightWeightBackendManager CreateManager(string folder)
        => new("file://" + folder, new Dictionary<string, string?>(), maxRetries: 2, retryDelay: 10, autoCreateFolders: true, retryWithExponentialBackoff: false, progressUpdater: null, backendProgressUpdater: null);

    private static async Task PutAsync(LightWeightBackendManager manager, string name)
    {
        using var data = new MemoryStream([1, 2, 3]);
        await manager.PutAsync(name, data, CancellationToken.None);
    }

    [Test]
    [Category("Targeted")]
    public async Task ADestinationThatDisappearsAfterItWasListedIsNotCreatedAgainAsync()
    {
        File.WriteAllBytes(Path.Combine(TARGETFOLDER, "existing"), [1]);
        var moved = TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-unplugged";

        using var manager = CreateManager(TARGETFOLDER);
        Assert.That(await manager.ListAsync(CancellationToken.None), Has.Count.EqualTo(1));

        Directory.Move(TARGETFOLDER, moved);
        try
        {
            Assert.CatchAsync<Exception>(async () => await PutAsync(manager, "copied"));
            Assert.That(Directory.Exists(TARGETFOLDER), Is.False, "The destination was created again after it disappeared");
        }
        finally
        {
            if (Directory.Exists(TARGETFOLDER))
                Directory.Delete(TARGETFOLDER, true);
            Directory.Move(moved, TARGETFOLDER);
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task AMissingDestinationIsStillCreatedAsync()
    {
        var folder = Path.Combine(TARGETFOLDER, "new");

        using var manager = CreateManager(folder);
        await PutAsync(manager, "copied");

        Assert.That(File.Exists(Path.Combine(folder, "copied")), Is.True);
    }
}
