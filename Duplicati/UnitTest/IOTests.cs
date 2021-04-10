﻿//  Copyright (C) 2018, The Duplicati Team
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
    }
}
