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
