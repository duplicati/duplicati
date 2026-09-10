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
using Duplicati.Library.AutoUpdater;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The data folder has to be an absolute path. An account with no home folder gets an empty
    /// application data folder from the operating system, and the Linux fallback that then takes
    /// over used to answer with the relative path "var/lib/Duplicati". Every consumer resolves such
    /// a path against its own working directory, and the server database stored it as part of a
    /// backup's database path, where it was joined onto the data folder a second time (issue #7284).
    ///
    /// The application data folder is injected, because the case cannot be provoked through the
    /// environment: clearing HOME still yields a folder through the password database.
    /// </summary>
    [TestFixture]
    [Category("DataFolderLocator")]
    public class DataFolderLocatorTests
    {
        /// <summary>
        /// An application name no installation can own, so neither candidate folder exists on the
        /// machine running the test and the branch outcome is fixed.
        /// </summary>
        private static string UniqueAppName() => $"duplicati-locator-test-{Guid.NewGuid():N}";

        /// <summary>
        /// Skips a test on the platforms where the fallback does not run.
        /// </summary>
        private static void RequireLinux()
        {
            if (!OperatingSystem.IsLinux())
                Assert.Ignore("The no-home-folder fallback is Linux-only.");
        }

        /// <summary>
        /// No home folder at all, and a home folder that is the root: both are the shapes that send
        /// the lookup into the fallback.
        /// </summary>
        [TestCase("")]
        [TestCase("/")]
        public void TheNoHomeFolderFallbackIsRooted(string applicationDataFolder)
        {
            RequireLinux();

            var appName = UniqueAppName();
            var folder = DataFolderLocator.GetDefaultStorageFolderInternal($"{appName}.sqlite", appName, applicationDataFolder);

            Assert.AreEqual(Path.Combine("/", "var", "lib", appName), folder);
            Assert.IsTrue(Path.IsPathRooted(folder), $"The data folder is not an absolute path: {folder}");
        }

        /// <summary>
        /// The other half of the fallback: an installation that predates /var/lib keeps the folder
        /// it is already using, and that one has to be rooted as well. The application name is "tmp"
        /// because /tmp is the only folder directly below the root that a test can write to.
        /// </summary>
        [Test]
        public void TheLegacyRootFolderIsRootedWhenItIsInUse()
        {
            RequireLinux();

            var targetfilename = $"duplicati-locator-test-{Guid.NewGuid():N}.sqlite";
            var probe = Path.Combine("/tmp", targetfilename);
            File.WriteAllText(probe, "");

            try
            {
                var folder = DataFolderLocator.GetDefaultStorageFolderInternal(targetfilename, "tmp", "");

                Assert.AreEqual("/tmp", folder);
                Assert.IsTrue(Path.IsPathRooted(folder), $"The data folder is not an absolute path: {folder}");
            }
            finally
            {
                File.Delete(probe);
            }
        }

        /// <summary>
        /// An ordinary account is not affected: the fallback only runs when the application data
        /// folder gives nothing to work with.
        /// </summary>
        [Test]
        public void AnApplicationDataFolderThatExistsIsUsedAsIs()
        {
            RequireLinux();

            var appName = UniqueAppName();
            var applicationDataFolder = Path.Combine("/home", "someone", ".config");

            Assert.AreEqual(Path.Combine(applicationDataFolder, appName),
                DataFolderLocator.GetDefaultStorageFolderInternal($"{appName}.sqlite", appName, applicationDataFolder));
        }
    }
}
