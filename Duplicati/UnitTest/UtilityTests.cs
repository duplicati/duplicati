//  Copyright (C) 2017, The Duplicati Team
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

using Duplicati.Library.Utility;
using NUnit.Framework;
using System;
using System.IO;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class UtilityTests
    {
        [Test]
        [Category("Utility")]
        public void GetFullPath()
        {
            string tempDir = Path.GetTempPath();

            string hasRelativeParts = Path.Combine(tempDir, "a", "b", "c", "..", "..", "d");
            string fullyQualified = Path.Combine(tempDir, "a", "d");
            Assert.AreEqual(fullyQualified, Utility.GetFullPath(hasRelativeParts));

            string hasRedundantSeparator = Path.Combine(tempDir, "a" + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar, "b");
            string noRedundantSeparator = Path.Combine(tempDir, "a", "b");
            Assert.AreEqual(noRedundantSeparator, Utility.GetFullPath(hasRedundantSeparator));

            string currentDirectory = Directory.GetCurrentDirectory();
            string filename = "file.txt";
            Assert.AreEqual(Path.Combine(currentDirectory, filename), Utility.GetFullPath(filename));

            if (Utility.IsClientLinux)
            {
                string hasTilde = Path.Combine("~", "a");
                string expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "a");
                Assert.AreEqual(expanded, Utility.GetFullPath(hasTilde));
            }
            else if (Utility.IsClientWindows)
            {
                string hasTilde = Path.Combine(@"c:\", "Progra~1", "file.txt");
                Assert.AreEqual(hasTilde, Utility.GetFullPath(hasTilde));
            }
        }
    }
}
