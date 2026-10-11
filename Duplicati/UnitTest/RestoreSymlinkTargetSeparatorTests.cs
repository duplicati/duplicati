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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.SourceProvider;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A relative symbolic link stores its target with the separators of the system it was backed up
/// on: <c>..\real.txt</c> on Windows, <c>../real.txt</c> elsewhere. Restored on the other system
/// as it was, the link does not resolve: Linux takes the backslash as part of the name, and Windows
/// does not resolve a slash in a link.
/// </summary>
public class RestoreSymlinkTargetSeparatorTests
{
    [Test]
    [Category("RestoreHandler")]
    // From Windows, restored elsewhere
    [TestCase(@"..\real.txt", true, false, "../real.txt")]
    [TestCase(@"sub\deeper\file.txt", true, false, "sub/deeper/file.txt")]
    [TestCase(@"C:\data\real.txt", true, false, @"C:\data\real.txt")]
    [TestCase(@"C:real.txt", true, false, @"C:real.txt")]
    [TestCase(@"\data\real.txt", true, false, @"\data\real.txt")]
    [TestCase(@"\\server\share\real.txt", true, false, @"\\server\share\real.txt")]
    [TestCase(@"\\?\C:\data\real.txt", true, false, @"\\?\C:\data\real.txt")]
    [TestCase("../real.txt", true, false, "../real.txt")]
    // From elsewhere, restored on Windows
    [TestCase("../real.txt", false, true, @"..\real.txt")]
    [TestCase("sub/deeper/file.txt", false, true, @"sub\deeper\file.txt")]
    [TestCase(@"../weird\name.txt", false, true, @"..\weird_name.txt")]
    [TestCase("/data/real.txt", false, true, "/data/real.txt")]
    // Restored on the system it was backed up on
    [TestCase(@"..\real.txt", true, true, @"..\real.txt")]
    [TestCase(@"name\with\backslash", false, false, @"name\with\backslash")]
    [TestCase("../real.txt", false, false, "../real.txt")]
    public void MapsOnlyRelativeTargetsFromTheOtherSystem(string target, bool sourceUsesWindowsSeparators, bool restoringOnWindows, string expected)
    {
        Assert.That(RestoreHandler.MapSymlinkTargetSeparators(target, sourceUsesWindowsSeparators, restoringOnWindows), Is.EqualTo(expected));
    }

    /// <summary>
    /// Restores a link backed up on the other system through the same call the restore makes, into a
    /// real folder, and reads the link back and the file through it. The restored paths map a backslash
    /// in a name from Linux to an underscore on Windows, so the link must point to that name.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    [TestCase(@"..\real.txt", "../real.txt", "real.txt", false)]
    [TestCase("../real.txt", @"..\real.txt", "real.txt", true)]
    [TestCase(@"../weird\name.txt", @"..\weird_name.txt", "weird_name.txt", true)]
    public async Task RestoredLinkFromTheOtherSystemResolves(string stored, string expected, string restoredFileName, bool onWindows)
    {
        if (OperatingSystem.IsWindows() != onWindows)
            Assert.Ignore(onWindows ? "Restores a link from Linux on Windows" : "Restores a link from Windows elsewhere");

        var root = Path.Combine(Path.GetTempPath(), "dup-symlink-sep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(root, restoredFileName), "real");
            var link = Path.Combine(root, "sub", "up");
            var metadata = Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, string> { ["CoreSymlinkTarget"] = stored });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(metadata));
            var options = new Options(new Dictionary<string, string?>());
            var destination = new FileRestoreDestinationProvider(root, false, false);

            try
            {
                await RestoreHandler.ApplyMetadataAsync(link, stream, options, destination, false, !onWindows, CancellationToken.None);
            }
            catch (Exception ex) when (onWindows && (ex is IOException || ex is UnauthorizedAccessException))
            {
                Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
            }

            Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(expected));
            Assert.That(File.ReadAllText(link), Is.EqualTo("real"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
