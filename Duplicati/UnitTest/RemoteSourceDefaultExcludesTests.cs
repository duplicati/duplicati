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
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The default excludes name the folders at the root of a drive, such as <c>?:\$Recycle.Bin\</c>.
/// A remote source, such as a SMB share, is mounted below a folder instead of on a drive, so
/// the $RECYCLE.BIN and System Volume Information folders at the root of the share were backed
/// up although the default excludes were selected (forum 22900)
/// </summary>
[TestFixture]
public class RemoteSourceDefaultExcludesTests
{
    private static string Mountpoint => OperatingSystem.IsWindows() ? @"X:\dupl-smb-l9g3v5q2\" : "/dupl-smb-l9g3v5q2/";

    private static IFilter ExpandWith(string filter, params string[] sources)
    {
        var (_, result) = Controller.ExpandInputSources(sources, new FilterExpression(filter, false), new Options(new Dictionary<string, string?>()));
        return result;
    }

    private static bool IsExcluded(IFilter filter, string path)
        => filter.Matches(path, out var include, out _) && !include;

    private static string InShare(params string[] parts)
        => Mountpoint + string.Join(Path.DirectorySeparatorChar, parts) + Path.DirectorySeparatorChar;

    [Test]
    [Category("Filter")]
    [TestCase("$RECYCLE.BIN")]
    [TestCase("System Volume Information")]
    public void TheDefaultExcludesApplyAtTheRootOfAShare(string folder)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The drive root entries of the default excludes are Windows filters");

        var filter = ExpandWith("{DefaultExcludes}", "@" + Mountpoint + "|smb://server/share");
        Assert.That(IsExcluded(filter, InShare(folder)), Is.True);
    }

    [Test]
    [Category("Filter")]
    public void AFolderOfTheSameNameDeeperInTheShareIsKept()
    {
        var filter = ExpandWith("{DefaultExcludes}", "@" + Mountpoint + "|smb://server/share");
        Assert.That(IsExcluded(filter, InShare("Data", "Recycled")), Is.False);
        Assert.That(IsExcluded(filter, InShare("Data")), Is.False);
    }

    [Test]
    [Category("Filter")]
    public void TheAddedEntriesKeepTheSignOfTheEntry()
    {
        var root = OperatingSystem.IsWindows() ? @"?:\Keep\" : "?:/Keep/";
        var filter = new FilterExpression(new[] { root }, true);
        var (_, result) = Controller.ExpandInputSources(["@" + Mountpoint + "|smb://server/share"], filter, new Options(new Dictionary<string, string?>()));

        Assert.That(result.Matches(InShare("Keep"), out var include, out _), Is.True);
        Assert.That(include, Is.True);
    }

    [Test]
    [Category("Filter")]
    public void WithoutARemoteSourceTheFilterIsNotChanged()
    {
        var source = Path.GetTempPath();
        var filter = new FilterExpression("{DefaultExcludes}", false);
        var (_, result) = Controller.ExpandInputSources([source], filter, new Options(new Dictionary<string, string?>()));
        Assert.That(FilterExpression.Serialize(result), Is.EqualTo(FilterExpression.Serialize(filter)));
    }
}
