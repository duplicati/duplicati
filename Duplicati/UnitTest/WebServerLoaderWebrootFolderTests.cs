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
using System.IO;
using Duplicati.Library.AutoUpdater;
using Duplicati.Server;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

[TestFixture]
public class WebServerLoaderWebrootFolderTests
{
    private static readonly string InstallationFolder = UpdaterManager.INSTALLATIONDIR;
    private static readonly string TrimmedInstallationFolder = Path.TrimEndingDirectorySeparator(InstallationFolder);

    private static bool IsAllowed(string? webroot, StringComparison comparison = StringComparison.Ordinal)
        => WebServerLoader.IsWebrootBelowFolder(webroot, InstallationFolder, comparison);

    [Test]
    public void FolderInsideInstallationFolderIsAllowed()
    {
        Assert.IsTrue(IsAllowed(Path.Combine(InstallationFolder, "customroot")));
        Assert.IsTrue(IsAllowed(Path.Combine(InstallationFolder, "customroot", "nested")));
        Assert.IsTrue(IsAllowed(Path.Combine(InstallationFolder, "customroot") + Path.DirectorySeparatorChar));
        Assert.IsTrue(IsAllowed(Path.Combine(InstallationFolder, "other", "..", "customroot")));
    }

    [Test]
    public void InstallationFolderItselfIsRejected()
    {
        Assert.IsFalse(IsAllowed(InstallationFolder));
        Assert.IsFalse(IsAllowed(TrimmedInstallationFolder));
        Assert.IsFalse(IsAllowed(Path.Combine(InstallationFolder, "customroot", "..")));
    }

    [Test]
    public void FolderOutsideInstallationFolderIsRejected()
    {
        Assert.IsFalse(IsAllowed(Path.GetDirectoryName(TrimmedInstallationFolder)));
        Assert.IsFalse(IsAllowed(Path.GetPathRoot(InstallationFolder)));
        // Not Path.GetTempPath(): other tests point TMP at a folder below the test binaries
        Assert.IsFalse(IsAllowed(Path.Combine(Path.GetDirectoryName(TrimmedInstallationFolder)!, "customroot")));
    }

    [Test]
    public void ParentReferencesThatLeaveInstallationFolderAreRejected()
    {
        Assert.IsFalse(IsAllowed(Path.Combine(InstallationFolder, "..", "outside")));
        Assert.IsFalse(IsAllowed(Path.Combine(InstallationFolder, "customroot", "..", "..", "outside")));
    }

    [Test]
    public void SiblingFolderSharingThePrefixIsRejected()
    {
        Assert.IsFalse(IsAllowed(TrimmedInstallationFolder + "2"));
        Assert.IsFalse(IsAllowed(Path.Combine(TrimmedInstallationFolder + "2", "customroot")));
    }

    [Test]
    public void EmptyAndInvalidValuesAreRejected()
    {
        Assert.IsFalse(IsAllowed(null));
        Assert.IsFalse(IsAllowed(""));
        Assert.IsFalse(IsAllowed("   "));
        Assert.IsFalse(IsAllowed(Path.Combine(InstallationFolder, "custom\0root")));
    }

    [Test]
    public void CaseIsComparedWithTheGivenComparison()
    {
        var webroot = Path.Combine(InstallationFolder.ToUpperInvariant(), "customroot");
        if (webroot.StartsWith(InstallationFolder, StringComparison.Ordinal))
            Assert.Ignore("The installation folder has no lower case letters");

        Assert.IsFalse(IsAllowed(webroot, StringComparison.Ordinal));
        Assert.IsTrue(IsAllowed(webroot, StringComparison.OrdinalIgnoreCase));
    }
}
