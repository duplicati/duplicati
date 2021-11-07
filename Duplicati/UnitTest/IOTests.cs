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

namespace Duplicati.UnitTest
{
    [Category("IO")]
    public class IOTests
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
            var filePathWithUNC = SystemIOWindows.PrefixWithUNC(filePath);

            var filePathWithUNCRoot = SystemIO.IO_WIN.GetPathRoot(filePathWithUNC);

            //Prefixed with UNC remains prefixed
            Assert.AreEqual(SystemIOWindows.PrefixWithUNC(root), filePathWithUNCRoot);

            //Without UNC prefixed, no prefix. 
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
        public void TestPrefixWithUNCInWindowsClient()
        {
            if (!Platform.IsClientWindows)
            {
                return;
            }

            // Normalization of basic relative paths, with both kinds of slashes
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"."),
                Is.EqualTo(@"."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"temp"),
                Is.EqualTo(@"temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"temp\file.txt"),
                Is.EqualTo(@"temp\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\"),
                Is.EqualTo(@"\"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"/"),
                Is.EqualTo(@"/"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\temp"),
                Is.EqualTo(@"\temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"/temp"),
                Is.EqualTo(@"/temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"/temp/file.txt"),
                Is.EqualTo(@"/temp/file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"/temp/file.txt"),
                Is.EqualTo(@"/temp/file.txt"));

            // Normalization of full qualified paths, but with relative components, with both kinds of slashes
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\."),
                Is.EqualTo(@"C:\temp\."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/."),
                Is.EqualTo(@"C:/temp/."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\.."),
                Is.EqualTo(@"C:\temp\.."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/.."),
                Is.EqualTo(@"C:/temp/.."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\..\folder"),
                Is.EqualTo(@"C:\temp\..\folder"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/../folder"),
                Is.EqualTo(@"C:/temp/../folder"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\.\file.txt"),
                Is.EqualTo(@"C:\temp\.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/./file.txt"),
                Is.EqualTo(@"C:/temp/./file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\."),
                Is.EqualTo(@"\\example.com\share\."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/."),
                Is.EqualTo(@"//example.com/share/."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\.."),
                Is.EqualTo(@"\\example.com\share\.."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/.."),
                Is.EqualTo(@"//example.com/share/.."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\..\folder"),
                Is.EqualTo(@"\\example.com\share\..\folder"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/../folder"),
                Is.EqualTo(@"//example.com/share/../folder"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\.\file.txt"),
                Is.EqualTo(@"\\example.com\share\.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/./file.txt"),
                Is.EqualTo(@"//example.com/share/./file.txt"));

            // Fully qualified paths with no relative components, with both kinds of slashes
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\"),
                Is.EqualTo(@"\\?\C:\"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/"),
                Is.EqualTo(@"\\?\C:\"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp"),
                Is.EqualTo(@"\\?\C:\temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp"),
                Is.EqualTo(@"\\?\C:\temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\file.txt"),
                Is.EqualTo(@"\\?\C:\temp\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/file.txt"),
                Is.EqualTo(@"\\?\C:\temp\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share"),
                Is.EqualTo(@"\\?\UNC\example.com\share"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share"),
                Is.EqualTo(@"\\?\UNC\example.com\share"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\file.txt"),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/file.txt"),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt"));

            // Fully qualified paths with no relative components, but with problematic names, with both kinds of slashes
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp."),
                Is.EqualTo(@"\\?\C:\temp."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp."),
                Is.EqualTo(@"\\?\C:\temp."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp.\file.txt"),
                Is.EqualTo(@"\\?\C:\temp.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp./file.txt"),
                Is.EqualTo(@"\\?\C:\temp.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp.\file.txt."),
                Is.EqualTo(@"\\?\C:\temp.\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp./file.txt."),
                Is.EqualTo(@"\\?\C:\temp.\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp "),
                Is.EqualTo(@"\\?\C:\temp "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp "),
                Is.EqualTo(@"\\?\C:\temp "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:\temp\file.txt "),
                Is.EqualTo(@"\\?\C:\temp\file.txt "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"C:/temp/file.txt "),
                Is.EqualTo(@"\\?\C:\temp\file.txt "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share."),
                Is.EqualTo(@"\\?\UNC\example.com\share."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share."),
                Is.EqualTo(@"\\?\UNC\example.com\share."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\file.txt."),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share./file.txt."),
                Is.EqualTo(@"\\?\UNC\example.com\share.\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share "),
                Is.EqualTo(@"\\?\UNC\example.com\share "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share "),
                Is.EqualTo(@"\\?\UNC\example.com\share "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\example.com\share\file.txt "),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"//example.com/share/file.txt "),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt "));

            // Normalization disabled for paths with @"\\?\" prefix
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\"),
                Is.EqualTo(@"\\?\C:\"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp"),
                Is.EqualTo(@"\\?\C:\temp"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp\file.txt"),
                Is.EqualTo(@"\\?\C:\temp\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp."),
                Is.EqualTo(@"\\?\C:\temp."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp.\file.txt"),
                Is.EqualTo(@"\\?\C:\temp.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp.\file.txt."),
                Is.EqualTo(@"\\?\C:\temp.\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp "),
                Is.EqualTo(@"\\?\C:\temp "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\temp\file.txt "),
                Is.EqualTo(@"\\?\C:\temp\file.txt "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\C:\"),
                Is.EqualTo(@"\\?\C:\"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share"),
                Is.EqualTo(@"\\?\UNC\example.com\share"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share\file.txt"),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share."),
                Is.EqualTo(@"\\?\UNC\example.com\share."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share.\file.txt"),
                Is.EqualTo(@"\\?\UNC\example.com\share.\file.txt"));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share.\file.txt."),
                Is.EqualTo(@"\\?\UNC\example.com\share.\file.txt."));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share "),
                Is.EqualTo(@"\\?\UNC\example.com\share "));
            Assert.That(
                SystemIOWindows.PrefixWithUNC(@"\\?\UNC\example.com\share\file.txt "),
                Is.EqualTo(@"\\?\UNC\example.com\share\file.txt "));
        }

        [Test]
        public void TestPathGetFullPathInWindowsClient()
        {
            if (!Platform.IsClientWindows)
            {
                return;
            }

            // Normalization of basic relative paths, with both kinds of slashes
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"."),
                Is.EqualTo(System.IO.Path.GetFullPath(@".")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"temp\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"temp\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"/"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"/")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"/temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"/temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"/temp/file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"/temp/file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"/temp/file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"/temp/file.txt")));

            // Normalization of full qualified paths, but with relative components, with both kinds of slashes
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\.."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\..")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/.."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/..")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\..\folder"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\..\folder")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/../folder"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/../folder")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\.\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\.\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/./file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/./file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\.."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\..")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/.."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/..")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\..\folder"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\..\folder")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/../folder"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/../folder")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\.\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\.\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/./file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/./file.txt")));

            // Fully qualified paths with no relative components, with both kinds of slashes
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/file.txt")));

            // Fully qualified paths with no relative components, but with problematic names, with both kinds of slashes
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:\temp.")).And.EqualTo(@"C:\temp."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:/temp.")).And.EqualTo(@"C:\temp."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp.\file.txt"),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:\temp.\file.txt")).And.EqualTo(@"C:\temp.\file.txt"));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp./file.txt"),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:/temp./file.txt")).And.EqualTo(@"C:\temp.\file.txt"));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp.\file.txt."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:\temp.\file.txt.")).And.EqualTo(@"C:\temp.\file.txt."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp./file.txt."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:/temp./file.txt.")).And.EqualTo(@"C:\temp.\file.txt."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:\temp ")).And.EqualTo(@"C:\temp "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:/temp ")).And.EqualTo(@"C:\temp "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:\temp\file.txt "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:\temp\file.txt ")).And.EqualTo(@"C:\temp\file.txt "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"C:/temp/file.txt "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"C:/temp/file.txt ")).And.EqualTo(@"C:\temp\file.txt "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share.")).And.EqualTo(@"\\example.com\share."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share.")).And.EqualTo(@"\\example.com\share."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\file.txt."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\file.txt.")).And.EqualTo(@"\\example.com\share\file.txt."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share./file.txt."),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share./file.txt.")).And.EqualTo(@"\\example.com\share.\file.txt."));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share ")).And.EqualTo(@"\\example.com\share "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share ")).And.EqualTo(@"\\example.com\share "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\example.com\share\file.txt "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"\\example.com\share\file.txt ")).And.EqualTo(@"\\example.com\share\file.txt "));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"//example.com/share/file.txt "),
                Is.Not.EqualTo(System.IO.Path.GetFullPath(@"//example.com/share/file.txt ")).And.EqualTo(@"\\example.com\share\file.txt "));

            // Normalization disabled for paths with @"\\?\" prefix
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp.\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp.\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp.\file.txt."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp.\file.txt.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp "),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp ")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\temp\file.txt "),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\temp\file.txt ")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\C:\"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\C:\")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share.\file.txt"),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share.\file.txt")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share.\file.txt."),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share.\file.txt.")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share "),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share ")));
            Assert.That(
                SystemIO.IO_WIN.PathGetFullPath(@"\\?\UNC\example.com\share\file.txt "),
                Is.EqualTo(System.IO.Path.GetFullPath(@"\\?\UNC\example.com\share\file.txt ")));
        }
    }
}
