//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

using System.Text;
using NUnit.Framework;

using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using Duplicati.Library.Common;
using System.Collections.Generic;

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
            var pathRoot =  Platform.IsClientWindows ? "C:\\" : "/";
            var root = SystemIO.IO_OS.GetPathRoot(LongPath(pathRoot));

            Assert.AreEqual(pathRoot, root);
        }

        [Test]
        public void TestUncBehaviourOfGetPathRoot()
        {
            if (!Platform.IsClientWindows)
            {
                return;
            }

            var root = @"C:" + Util.DirectorySeparatorString;
            var filename = "test.txt";
            var filePath = root + filename;
            var filePathWithExtendedDevicePathPrefix = SystemIOWindows.AddExtendedDevicePathPrefix(filePath);

            var filePathWithExtendedDevicePathPrefixRoot = SystemIO.IO_WIN.GetPathRoot(filePathWithExtendedDevicePathPrefix);

            //Prefixed with extended device path prefix remains prefixed
            Assert.AreEqual(SystemIOWindows.AddExtendedDevicePathPrefix(root), filePathWithExtendedDevicePathPrefixRoot);

            //Without extended device path prefix, no prefix. 
            var filePathRoot = SystemIO.IO_WIN.GetPathRoot(filePath);
            Assert.AreEqual(root, filePathRoot);
        }

        [Test]
        public void TestGetFilesWhenDirectoryDoesNotExist()
        {
            var pathRoot = Platform.IsClientWindows ? "C:\\" : "/";

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
            var pathRoot = Platform.IsClientWindows ? "C:\\" : "/";

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
            if (!Platform.IsClientWindows)
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
            if (!Platform.IsClientWindows)
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
                var actual = SystemIO.IO_WIN.PathGetFullPath(path); 
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
                var actual = SystemIO.IO_WIN.PathGetFullPath(path);
                var expected = keyValuePair.Value;
                Assert.AreEqual(expected, actual, $"Path: {path}");
            }
        }
    }
}