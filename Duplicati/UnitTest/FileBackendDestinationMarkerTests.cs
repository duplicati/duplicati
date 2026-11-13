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

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The file backend only uses a destination folder that has the marker file given by
/// --alternate-destination-marker, also when no alternate paths are given (#1520), so a backup
/// does not write into an empty mount point when the share is not mounted.
/// </summary>
[TestFixture]
public class FileBackendDestinationMarkerTests
{
    private const string Marker = "duplicati-destination.marker";
    private DirectoryInfo m_root = null!;

    [SetUp]
    public void CreateRoot() => m_root = Directory.CreateTempSubdirectory("marker-test-");

    [TearDown]
    public void DeleteRoot() => m_root.Delete(true);

    private string Folder(string name, bool withMarker)
    {
        var folder = Directory.CreateDirectory(Path.Combine(m_root.FullName, name)).FullName;
        if (withMarker)
            File.WriteAllText(Path.Combine(folder, Marker), "");
        return folder;
    }

    private static Library.Backend.File Backend(string folder, Dictionary<string, string?> options)
        => new("file://" + folder, options);

    private static async Task PutAsync(Library.Backend.File backend, string name)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("contents"));
        await backend.PutAsync(name, data, CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    [Category("Backend")]
    public void AMarkerAloneRefusesAFolderWithoutIt()
    {
        var folder = Folder("unmounted", false);

        var error = Assert.Throws<UserInformationException>(() => Backend(folder, new() { ["alternate-destination-marker"] = Marker }).Dispose());
        Assert.That(error!.HelpID, Is.EqualTo("NoDestinationWithMarker"));
        Assert.That(error.Message, Does.Contain(folder));
    }

    [Test]
    [Category("Backend")]
    public void AMarkerAloneRefusesAMissingFolder()
    {
        var folder = Path.Combine(m_root.FullName, "missing");

        var error = Assert.Throws<UserInformationException>(() => Backend(folder, new() { ["alternate-destination-marker"] = Marker }).Dispose());
        Assert.That(error!.HelpID, Is.EqualTo("NoDestinationWithMarker"));
        Assert.That(Directory.Exists(folder), Is.False, "The folder was created");
    }

    [Test]
    [Category("Backend")]
    public async Task AMarkerAloneAcceptsAFolderWithItAsync()
    {
        var folder = Folder("mounted", true);

        using (var backend = Backend(folder, new() { ["alternate-destination-marker"] = Marker }))
            await PutAsync(backend, "a.txt").ConfigureAwait(false);

        Assert.That(File.Exists(Path.Combine(folder, "a.txt")), Is.True);
    }

    [Test]
    [Category("Backend")]
    public async Task AnAlternatePathWithTheMarkerIsStillChosenAsync()
    {
        var primary = Folder("primary", false);
        var alternate = Folder("alternate", true);

        using (var backend = Backend(primary, new()
        {
            ["alternate-destination-marker"] = Marker,
            ["alternate-target-paths"] = alternate,
        }))
            await PutAsync(backend, "a.txt").ConfigureAwait(false);

        Assert.That(File.Exists(Path.Combine(alternate, "a.txt")), Is.True, "The alternate path has the marker");
        Assert.That(File.Exists(Path.Combine(primary, "a.txt")), Is.False, "The primary path has no marker");
    }
}
