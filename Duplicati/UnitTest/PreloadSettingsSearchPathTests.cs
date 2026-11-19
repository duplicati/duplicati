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
using System.IO;
using System.Linq;
using Duplicati.Library.AutoUpdater;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The server reports the paths searched for preload settings, so a user can tell where
/// a preload.json is read from (issue #6611).
/// </summary>
public class PreloadSettingsSearchPathTests
{
    private static readonly string[] DefaultPaths = [Path.Combine(Path.GetTempPath(), "data", "preload.json"), Path.Combine(Path.GetTempPath(), "install", "preload.json")];
    private static readonly string[] PortablePaths = [Path.Combine(Path.GetTempPath(), "install", "preload.json")];

    [Test]
    public void TheDefaultPathsAreSearchedOutsidePortableMode()
        => Assert.That(PreloadSettingsLoader.GetSearchPaths(false, "", DefaultPaths, PortablePaths), Is.EqualTo((DefaultPaths, false)));

    [Test]
    public void OnlyThePortablePathsAreSearchedInPortableMode()
        => Assert.That(PreloadSettingsLoader.GetSearchPaths(true, "", DefaultPaths, PortablePaths), Is.EqualTo((PortablePaths, false)));

    [TestCase(false)]
    [TestCase(true)]
    public void AnExclamationMarkInTheVariableReplacesAllOtherPaths(bool portableMode)
    {
        var file = Path.Combine(Path.GetTempPath(), "custom", "preload.json");
        var (paths, isExplicitOverride) = PreloadSettingsLoader.GetSearchPaths(portableMode, "!" + file, DefaultPaths, PortablePaths);
        Assert.That(paths, Is.EqualTo(new[] { file }));
        Assert.That(isExplicitOverride, Is.True);
    }

    [Test]
    public void OnlyRootedPathsThatExistAreReportedAsRead()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"preload-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var existing = Path.Combine(folder, "preload.json");
            File.WriteAllText(existing, "{}");
            var missing = Path.Combine(folder, "missing", "preload.json");

            var described = PreloadSettingsLoader.DescribeSearchPaths([existing, missing, "preload.json"]);

            Assert.That(described.Select(x => (x.Path, x.Exists)), Is.EqualTo(new[] { (existing, true), (missing, false), ("preload.json", false) }));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void TheReportedPathsIncludeTheInstallationFolder()
    {
        var installPath = Path.Combine(Path.GetDirectoryName(typeof(PreloadSettingsLoader).Assembly.Location) ?? "", "preload.json");
        Assert.That(PreloadSettingsLoader.GetPreloadSettingsPaths().Select(x => x.Path), Does.Contain(installPath));
    }
}
