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
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Operation.Backup;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The --one-file-system option leaves out the folders below a source where another file
    /// system is mounted. Issue #1530.
    /// </summary>
    [TestFixture]
    public class OneFileSystemTests : BasicSetupHelper
    {
        /// <summary>
        /// Builds an absolute path for the current platform from its parts.
        /// </summary>
        private static string P(params string[] parts)
            => OperatingSystem.IsWindows()
                ? "C:\\" + string.Join("\\", parts)
                : "/" + string.Join("/", parts);

        [Test]
        public void OnlyMountPointsBelowASourceAreReturned()
        {
            var source = Util.AppendDirSeparator(P("data"));
            var mountPoints = new[]
            {
                // The root of the system and an ancestor of the source are never reached
                OperatingSystem.IsWindows() ? "C:\\" : "/",
                P("data"),
                P("data", "disk"),
                P("data", "deep", "nfs") + Path.DirectorySeparatorChar,
                // A sibling that shares the start of the name is not below the source
                P("data2"),
                P("other"),
            };

            var result = OtherFileSystems.GetMountPointsBelowSources([source], mountPoints);

            CollectionAssert.AreEquivalent(
                new[] { Util.AppendDirSeparator(P("data", "disk")), Util.AppendDirSeparator(P("data", "deep", "nfs")) },
                result);
        }

        [Test]
        public void ASourceWithoutTrailingSeparatorIsTreatedAsAFolder()
        {
            var result = OtherFileSystems.GetMountPointsBelowSources([P("data")], [P("data", "disk")]);
            CollectionAssert.AreEqual(new[] { Util.AppendDirSeparator(P("data", "disk")) }, result);
        }

        [Test]
        public void RemoteSourcesAreSkipped()
        {
            var mountpoint = Util.AppendDirSeparator(P("dupl-ftp-abcdefgh"));
            var result = OtherFileSystems.GetMountPointsBelowSources(["@" + mountpoint + "|ftp://example.com/folder"], [P("dupl-ftp-abcdefgh", "inner")]);
            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void TheSystemRootIsListedAsAMountPoint()
        {
            var root = Path.GetPathRoot(Path.GetTempPath())!;
            var mountPoints = OtherFileSystems.ListMountPoints().Select(m => Util.AppendDirSeparator(m)).ToList();
            CollectionAssert.Contains(mountPoints, Util.AppendDirSeparator(root), "Listed: " + string.Join(", ", mountPoints));
        }

        /// <summary>
        /// The environment variable naming a source folder that has another file system mounted
        /// in its sub-folder "mounted". Creating a mount needs administrator rights, so this test
        /// only runs where one has been set up.
        /// </summary>
        private const string MOUNTED_SOURCE_VARIABLE = "DUPLICATI_TEST_ONE_FILE_SYSTEM_SOURCE";

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public async Task FolderOnAnotherFileSystemIsLeftOut(bool oneFileSystem)
        {
            var source = Environment.GetEnvironmentVariable(MOUNTED_SOURCE_VARIABLE);
            if (string.IsNullOrWhiteSpace(source))
                Assert.Ignore($"Set {MOUNTED_SOURCE_VARIABLE} to a folder with another file system mounted at its sub-folder \"mounted\"");

            source = Util.AppendDirSeparator(source);
            var mounted = Util.AppendDirSeparator(Path.Combine(source, "mounted"));
            var outside = Path.Combine(source, "outside.txt");
            var inside = Path.Combine(mounted, "inside.txt");
            File.WriteAllText(outside, "outside");
            File.WriteAllText(inside, "inside");

            // On Windows a volume mounted in a folder is a reparse point, which the default
            // symlink policy stores as a link without going in, so the links are followed here
            var opts = TestOptions.Expand(new { one_file_system = oneFileSystem, symlink_policy = "Follow" });
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts, null))
                TestUtils.AssertResults(await c.BackupAsync([source]));

            string[] paths;
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts, null))
                paths = (await c.ListAsync("*")).Files.Select(f => f.Path).ToArray();

            CollectionAssert.Contains(paths, source, "The source folder is always included");
            CollectionAssert.Contains(paths, outside);
            if (oneFileSystem)
            {
                CollectionAssert.DoesNotContain(paths, mounted);
                CollectionAssert.DoesNotContain(paths, inside);
            }
            else
            {
                CollectionAssert.Contains(paths, mounted);
                CollectionAssert.Contains(paths, inside);
            }
        }
    }
}
