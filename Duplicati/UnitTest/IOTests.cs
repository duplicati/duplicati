// Copyright (C) 2025, The Duplicati Team
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

using System.Text;
using NUnit.Framework;

using Duplicati.Library.Common.IO;
using System.Collections.Generic;
using System;

namespace Duplicati.UnitTest
{
    [Category("IO")]
    public class IOTests : BasicSetupHelper
    {

        public static string LongPath(string pathRoot)
        {
            string subFolder = "test";
            return pathRoot + new StringBuilder().Insert(0, subFolder + Util.DirectorySeparatorString, 100);
        }

        [Test]
        public void TestGetPathRootWithLongPath()
        {
            var pathRoot = OperatingSystem.IsWindows() ? "C:\\" : "/";
            var root = SystemIO.IO_OS.GetPathRoot(LongPath(pathRoot));

            Assert.AreEqual(pathRoot, root);
        }

        [Test]
        public void TestUncBehaviourOfGetPathRoot()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var root = @"C:" + Util.DirectorySeparatorString;
            var filename = "test.txt";
            var filePath = root + filename;
            var filePathWithExtendedDevicePathPrefix = SystemIOWindows.AddExtendedDevicePathPrefix(filePath);

            var filePathWithExtendedDevicePathPrefixRoot = SystemIO.IO_OS.GetPathRoot(filePathWithExtendedDevicePathPrefix);

            //Prefixed with extended device path prefix remains prefixed
            Assert.AreEqual(SystemIOWindows.AddExtendedDevicePathPrefix(root), filePathWithExtendedDevicePathPrefixRoot);

            //Without extended device path prefix, no prefix. 
            var filePathRoot = SystemIO.IO_OS.GetPathRoot(filePath);
            Assert.AreEqual(root, filePathRoot);
        }

        [Test]
        public void TestGetFilesWhenDirectoryDoesNotExist()
        {
            var pathRoot = OperatingSystem.IsWindows() ? "C:\\" : "/";

            var longPath = LongPath(pathRoot);
            if (SystemIO.IO_OS.DirectoryExists(longPath))
            {
                return;
            }

            //In particular don't throw PathTooLongException
            Assert.Throws<System.IO.DirectoryNotFoundException>(() => SystemIO.IO_OS.GetFiles(longPath));
        }

        [Test]
        public void TestGetDirectoriesWhenDirectoryDoesNotExist()
        {
            var pathRoot = OperatingSystem.IsWindows() ? "C:\\" : "/";

            var longPath = LongPath(pathRoot);
            if (SystemIO.IO_OS.DirectoryExists(longPath))
            {
                return;
            }

            //In particular don't throw PathTooLongException
            Assert.Throws<System.IO.DirectoryNotFoundException>(() => SystemIO.IO_OS.GetDirectories(longPath));
        }

        [Test]
        public void TestAddExtendedDevicePathPrefixInWindowsClient()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var testCasesLeavingPathUnchanged =
                new[]
                {
                    // Normalization of basic relative paths, with both kinds of slashes
                    @".",
                    @"temp",
                    @"temp\file.txt",
                    @"\",
                    @"/",
                    @"\temp",
                    @"/temp",
                    @"/temp/file.txt",

                    // Normalization of full qualified paths, but with relative components, with both kinds of slashes
                    @"C:\temp\.",
                    @"C:/temp/.",
                    @"C:\temp\..",
                    @"C:/temp/..",
                    @"C:\temp\..\folder",
                    @"C:/temp/../folder",
                    @"C:\temp\.\file.txt",
                    @"C:/temp/./file.txt",
                    @"\\example.com\share\.",
                    @"//example.com/share/.",
                    @"\\example.com\share\..",
                    @"//example.com/share/..",
                    @"\\example.com\share\..\folder",
                    @"//example.com/share/../folder",
                    @"\\example.com\share\.\file.txt",
                    @"//example.com/share/./file.txt",

                    // Normalization disabled for paths with @"\\?\" prefix
                    @"\\?\C:\",
                    @"\\?\C:\temp",
                    @"\\?\C:\temp\file.txt",
                    @"\\?\C:\temp.",
                    @"\\?\C:\temp.\file.txt",
                    @"\\?\C:\temp.\file.txt.",
                    @"\\?\C:\temp ",
                    @"\\?\C:\temp\file.txt ",
                    @"\\?\C:\",
                    @"\\?\UNC\example.com\share",
                    @"\\?\UNC\example.com\share\file.txt",
                    @"\\?\UNC\example.com\share.",
                    @"\\?\UNC\example.com\share.\file.txt",
                    @"\\?\UNC\example.com\share.\file.txt.",
                    @"\\?\UNC\example.com\share ",
                    @"\\?\UNC\example.com\share\file.txt "
                };
            foreach (var path in testCasesLeavingPathUnchanged)
            {
                var actual = SystemIOWindows.AddExtendedDevicePathPrefix(path);
                var expected = path;
                Assert.AreEqual(expected, actual, $"Path: {path}");
            }

            var testCasesWherePrefixIsApplied =
                new Dictionary<string, string>()
                {
                    // Fully qualified paths with no relative components, with both kinds of slashes
                    { @"C:\", @"\\?\C:\" },
                    { @"C:/", @"\\?\C:\" },
                    { @"C:\temp", @"\\?\C:\temp" },
                    { @"C:/temp", @"\\?\C:\temp" },
                    { @"C:\temp\file.txt", @"\\?\C:\temp\file.txt" },
                    { @"C:/temp/file.txt", @"\\?\C:\temp\file.txt" },
                    { @"\\example.com\share", @"\\?\UNC\example.com\share" },
                    { @"//example.com/share", @"\\?\UNC\example.com\share" },
                    { @"\\example.com\share\file.txt", @"\\?\UNC\example.com\share\file.txt" },
                    { @"//example.com/share/file.txt", @"\\?\UNC\example.com\share\file.txt" },
                    
                    // Fully qualified paths with no relative components, but with problematic names, with both kinds of slashes
                    { @"C:\temp.", @"\\?\C:\temp." },
                    { @"C:/temp.", @"\\?\C:\temp." },
                    { @"C:\temp.\file.txt", @"\\?\C:\temp.\file.txt" },
                    { @"C:/temp./file.txt", @"\\?\C:\temp.\file.txt" },
                    { @"C:\temp.\file.txt.", @"\\?\C:\temp.\file.txt." },
                    { @"C:/temp./file.txt.", @"\\?\C:\temp.\file.txt." },
                    { @"C:\temp ", @"\\?\C:\temp " },
                    { @"C:/temp ", @"\\?\C:\temp " },
                    { @"C:\temp\file.txt ", @"\\?\C:\temp\file.txt " },
                    { @"C:/temp/file.txt ", @"\\?\C:\temp\file.txt " },
                    { @"\\example.com\share.", @"\\?\UNC\example.com\share." },
                    { @"//example.com/share.", @"\\?\UNC\example.com\share." },
                    { @"\\example.com\share\file.txt.", @"\\?\UNC\example.com\share\file.txt." },
                    { @"//example.com/share./file.txt.", @"\\?\UNC\example.com\share.\file.txt." },
                    { @"\\example.com\share ", @"\\?\UNC\example.com\share " },
                    { @"//example.com/share ", @"\\?\UNC\example.com\share " },
                    { @"\\example.com\share\file.txt ", @"\\?\UNC\example.com\share\file.txt " },
                    { @"//example.com/share/file.txt ", @"\\?\UNC\example.com\share\file.txt " }
                };
            foreach (var keyValuePair in testCasesWherePrefixIsApplied)
            {
                var path = keyValuePair.Key;
                var actual = SystemIOWindows.AddExtendedDevicePathPrefix(path);
                var expected = keyValuePair.Value;
                Assert.AreEqual(expected, actual, $"Path: {path}");
            }
        }

        [Test]
        public void TestPathGetFullPathInWindowsClient()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var testCasesWherePathGetFullGivesSameResultsAsDotNet =
                new[]
                {
                    // Normalization of basic relative paths, with both kinds of slashes
                    @".",
                    @"temp",
                    @"temp\file.txt",
                    @"\",
                    @"/",
                    @"\temp",
                    @"/temp",
                    @"/temp/file.txt",
                    @"/temp/file.txt",
                    
                    // Normalization of full qualified paths, but with relative components, with both kinds of slashes
                    @"C:\temp\.",
                    @"C:/temp/.",
                    @"C:\temp\..",
                    @"C:/temp/..",
                    @"C:\temp\..\folder",
                    @"C:/temp/../folder",
                    @"C:\temp\.\file.txt",
                    @"C:/temp/./file.txt",
                    @"\\example.com\share\.",
                    @"//example.com/share/.",
                    @"\\example.com\share\..",
                    @"//example.com/share/..",
                    @"\\example.com\share\..\folder",
                    @"//example.com/share/../folder",
                    @"\\example.com\share\.\file.txt",
                    @"//example.com/share/./file.txt",
                    
                    // Fully qualified paths with no relative components, with both kinds of slashes
                    @"C:\",
                    @"C:/",
                    @"C:\temp",
                    @"C:/temp",
                    @"C:\temp\file.txt",
                    @"C:/temp/file.txt",
                    @"\\example.com\share",
                    @"//example.com/share",
                    @"\\example.com\share\file.txt",
                    @"//example.com/share/file.txt",
                    
                    // Normalization disabled for paths with @"\\?\" prefix
                    @"\\?\C:\",
                    @"\\?\C:\temp",
                    @"\\?\C:\temp\file.txt",
                    @"\\?\C:\temp.",
                    @"\\?\C:\temp.\file.txt",
                    @"\\?\C:\temp.\file.txt.",
                    @"\\?\C:\temp ",
                    @"\\?\C:\temp\file.txt ",
                    @"\\?\C:\",
                    @"\\?\UNC\example.com\share",
                    @"\\?\UNC\example.com\share\file.txt",
                    @"\\?\UNC\example.com\share.",
                    @"\\?\UNC\example.com\share.\file.txt",
                    @"\\?\UNC\example.com\share.\file.txt.",
                    @"\\?\UNC\example.com\share ",
                    @"\\?\UNC\example.com\share\file.txt ",
                };
            foreach (var path in testCasesWherePathGetFullGivesSameResultsAsDotNet)
            {
                var actual = SystemIO.IO_OS.PathGetFullPath(path);
                var expected = System.IO.Path.GetFullPath(path);
                Assert.AreEqual(expected, actual, $"Path: {path}");
            }

            var testCasesWherePathGetFullGivesDifferentResultsThanDotNet =
                new Dictionary<string, string>()
                {
                    // Fully qualified paths with no relative components, but with problematic names, with both kinds of slashes
                    { @"C:\temp.", @"C:\temp." },
                    { @"C:/temp.", @"C:\temp." },
                    { @"C:\temp.\file.txt", @"C:\temp.\file.txt" },
                    { @"C:/temp./file.txt", @"C:\temp.\file.txt" },
                    { @"C:\temp.\file.txt.", @"C:\temp.\file.txt." },
                    { @"C:/temp./file.txt.", @"C:\temp.\file.txt." },
                    { @"C:\temp ", @"C:\temp " },
                    { @"C:/temp ", @"C:\temp " },
                    { @"C:\temp\file.txt ", @"C:\temp\file.txt " },
                    { @"C:/temp/file.txt ", @"C:\temp\file.txt " },
                    { @"\\example.com\share.", @"\\example.com\share." },
                    { @"//example.com/share.", @"\\example.com\share." },
                    { @"\\example.com\share\file.txt.", @"\\example.com\share\file.txt." },
                    { @"//example.com/share./file.txt.", @"\\example.com\share.\file.txt." },
                    { @"\\example.com\share ", @"\\example.com\share " },
                    { @"//example.com/share ", @"\\example.com\share " },
                    { @"\\example.com\share\file.txt ", @"\\example.com\share\file.txt " },
                    { @"//example.com/share/file.txt ", @"\\example.com\share\file.txt " },
                };
            foreach (var keyValuePair in testCasesWherePathGetFullGivesDifferentResultsThanDotNet)
            {
                var path = keyValuePair.Key;
                var actual = SystemIO.IO_OS.PathGetFullPath(path);
                var expected = keyValuePair.Value;
                Assert.AreEqual(expected, actual, $"Path: {path}");
            }
        }
    }
}