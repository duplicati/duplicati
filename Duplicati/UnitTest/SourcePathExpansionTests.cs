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
using System.IO;
using System.Linq;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A bare drive specification such as "C:" is drive-relative on Windows, so
/// Path.GetFullPath answers with the process' current directory on that drive
/// instead of the root of it. A source given that way was silently replaced by
/// whatever directory the process happened to be in, and the drive wildcard
/// "*:" builds that shape itself: 'C' + ":" is what it hands on.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SourcePathExpansionTests
{
    /// <summary>
    /// The drive of the given path as a bare specification ("C:"), or null when
    /// the path does not sit on a lettered drive
    /// </summary>
    /// <param name="path">The path to read the drive from</param>
    /// <returns>The bare drive specification, or null</returns>
    private static string? DriveSpecificationOf(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root == null || root.Length != 3 || root[1] != ':' || !char.IsLetter(root[0]))
            return null;

        return root.Substring(0, 2);
    }

    /// <summary>
    /// Runs the expansion from a known current directory, because the fault is
    /// that the current directory is what a bare drive resolves to
    /// </summary>
    /// <param name="inputsource">The source to expand</param>
    /// <param name="from">The directory to stand in while expanding</param>
    /// <returns>The expanded sources</returns>
    private static string[] ExpandFrom(string inputsource, string from)
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(from);
        try
        {
            return Controller.ExpandInputSources([inputsource], null, new Options(new Dictionary<string, string?>())).Sources;
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    /// <summary>
    /// A source that sits inside another source is normally taken out of the list and
    /// replaced by an include filter. That cannot work when a folder on the way to it is
    /// excluded, because the walk of the outer source stops there and never reaches the
    /// filter, so such a source is kept instead. Issue #3220.
    /// </summary>
    /// <param name="excludeNested">Whether the filter excludes the nested source or the folder above it</param>
    /// <param name="expectKept">Whether the nested source is expected to survive</param>
    [Test]
    [Category("Controller")]
    [TestCase(false, true, TestName = "ASourceUnderAnExcludedFolderIsKept")]
    [TestCase(true, false, TestName = "ASourceThatIsItselfExcludedIsStillReplacedByAFilter")]
    public void ANestedSourceIsOnlyKeptWhenNothingElseCanReachIt(bool excludeNested, bool expectKept)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var middle = Path.Combine(root, "middle");
        var nested = Path.Combine(middle, "nested");
        Directory.CreateDirectory(nested);

        try
        {
            var filter = new Library.Utility.FilterExpression(
                Util.AppendDirSeparator(excludeNested ? nested : middle), false);

            var (sources, resulting) = Controller.ExpandInputSources(
                [root, nested], filter, new Options(new Dictionary<string, string?>()));

            var kept = sources.Contains(Util.AppendDirSeparator(nested), Library.Utility.Utility.ClientFilenameStringComparer);
            Assert.AreEqual(expectKept, kept,
                $"sources: {string.Join(", ", sources)}");

            if (expectKept)
                // Nothing is added to the filter, so an argument that only excludes stays
                // that way and the outer walk still stops where it was told to
                Assert.AreSame(filter, resulting, "the filter should be left alone when the source is kept");
            else
                Assert.AreNotSame(filter, resulting, "the source should have become an include filter");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Green before and after: with nothing excluded there is nothing that can hide the
    /// nested source, so it is taken out as it always was
    /// </summary>
    [Test]
    [Category("Controller")]
    public void ANestedSourceWithNoFilterIsStillRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var nested = Path.Combine(root, "middle", "nested");
        Directory.CreateDirectory(nested);

        try
        {
            var sources = Controller.ExpandInputSources(
                [root, nested], null, new Options(new Dictionary<string, string?>())).Sources;

            Assert.AreEqual(1, sources.Length, $"sources: {string.Join(", ", sources)}");
            Assert.AreEqual(Util.AppendDirSeparator(root), sources[0]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    [Category("Controller")]
    public void ABareDriveMeansTheRootOfIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A drive specification is only one on Windows.");
            return;
        }

        Assert.AreEqual(@"C:\", Util.ExpandBareDriveRoot("C:"));
        Assert.AreEqual(@"c:\", Util.ExpandBareDriveRoot("c:"));
    }

    [Test]
    [Category("Controller")]
    public void APathThatAlreadySaysWhereItStartsIsLeftAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A drive specification is only one on Windows.");
            return;
        }

        Assert.AreEqual(@"C:\", Util.ExpandBareDriveRoot(@"C:\"));
        Assert.AreEqual(@"C:\Users", Util.ExpandBareDriveRoot(@"C:\Users"));
        Assert.AreEqual(@"\\server\share", Util.ExpandBareDriveRoot(@"\\server\share"));
    }

    /// <summary>
    /// "C:foo" is drive-relative as well, but it is also a plausible thing to
    /// mean, so it is left as it is
    /// </summary>
    [Test]
    [Category("Controller")]
    public void APathWithSomethingAfterTheDriveIsLeftAlone()
    {
        Assert.AreEqual("C:foo", Util.ExpandBareDriveRoot("C:foo"));
        Assert.AreEqual("foo", Util.ExpandBareDriveRoot("foo"));
        Assert.AreEqual("", Util.ExpandBareDriveRoot(""));
    }

    [Test]
    [Category("Controller")]
    public void ElsewhereADriveSpecificationIsAnOrdinaryName()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This is about the platforms where a colon is an ordinary character.");
            return;
        }

        Assert.AreEqual("C:", Util.ExpandBareDriveRoot("C:"));
    }

    /// <summary>
    /// What all of the above is for: a source given as a bare drive has to reach
    /// the backup as the root of that drive, not as wherever the process stands
    /// </summary>
    [Test]
    [Category("Controller")]
    public void ASourceGivenAsABareDriveExpandsToTheDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A drive specification is only one on Windows.");
            return;
        }

        var standIn = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var drive = DriveSpecificationOf(standIn);
        if (drive == null)
        {
            Assert.Ignore("The temporary folder is not on a lettered drive.");
            return;
        }

        Directory.CreateDirectory(standIn);
        try
        {
            var sources = ExpandFrom(drive, standIn);

            Assert.AreEqual(1, sources.Length);
            Assert.AreEqual(drive + Path.DirectorySeparatorChar, sources[0]);
        }
        finally
        {
            Directory.Delete(standIn, true);
        }
    }

    /// <summary>
    /// Green before and after: the ordinary spellings were never affected, so a
    /// failure here would mean the change reached further than the fault did
    /// </summary>
    [Test]
    [Category("Controller")]
    public void TheOrdinarySpellingsAreUnchanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A drive specification is only one on Windows.");
            return;
        }

        var standIn = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var drive = DriveSpecificationOf(standIn);
        if (drive == null)
        {
            Assert.Ignore("The temporary folder is not on a lettered drive.");
            return;
        }

        Directory.CreateDirectory(standIn);
        try
        {
            var root = drive + Path.DirectorySeparatorChar;
            Assert.AreEqual(root, ExpandFrom(root, standIn).Single());

            var named = Util.AppendDirSeparator(standIn);
            Assert.AreEqual(named, ExpandFrom(named, standIn).Single());
            Assert.AreEqual(named, ExpandFrom(standIn, standIn).Single());
        }
        finally
        {
            Directory.Delete(standIn, true);
        }
    }
}
