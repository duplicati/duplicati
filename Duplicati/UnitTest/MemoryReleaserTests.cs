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
using System.Runtime.CompilerServices;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Serialization;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the server gives back the memory of a finished operation
/// </summary>
[TestFixture]
public class MemoryReleaserTests
{
    [Test]
    [Category("Utility")]
    public void ReleaseUnusedMemoryCollectsDroppedLargeObjects()
    {
        // A dropped large object is not collected while the process is idle
        var dropped = AllocateAndDrop();

        MemoryReleaser.ReleaseUnusedMemory();

        Assert.IsFalse(dropped.IsAlive, "The dropped buffer was not collected");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AllocateAndDrop()
        => new WeakReference(new byte[16 * 1024 * 1024]);

    [Test]
    [Category("Utility")]
    public void ReleaseUnusedMemoryCanRunRepeatedly()
    {
        // The second call takes the path where malloc_trim was already found or found missing
        Assert.DoesNotThrow(MemoryReleaser.ReleaseUnusedMemory);
        Assert.DoesNotThrow(MemoryReleaser.ReleaseUnusedMemory);
    }

    [TestCase(DuplicatiOperation.BackupOrSync)]
    [TestCase(DuplicatiOperation.Restore)]
    [TestCase(DuplicatiOperation.Repair)]
    [TestCase(DuplicatiOperation.Compact)]
    [TestCase(DuplicatiOperation.Verify)]
    [Category("Utility")]
    public void LargeOperationsReleaseMemoryAfterwards(DuplicatiOperation operation)
        => Assert.IsTrue(Runner.ReleasesMemoryAfterwards(operation));

    [TestCase(DuplicatiOperation.List)]
    [TestCase(DuplicatiOperation.ListFilesets)]
    [TestCase(DuplicatiOperation.ListFolderContents)]
    [TestCase(DuplicatiOperation.ListFileVersions)]
    [TestCase(DuplicatiOperation.SearchEntries)]
    [TestCase(DuplicatiOperation.CustomRunner)]
    [Category("Utility")]
    public void BrowsingOperationsDoNotCollect(DuplicatiOperation operation)
        => Assert.IsFalse(Runner.ReleasesMemoryAfterwards(operation));
}
