//  Copyright (C) 2015, The Duplicati Team
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
using NUnit.Framework;using System.Linq;using System.IO;
using System;using System.Collections.Generic;

namespace Duplicati.UnitTest
{
    [TestFixture()]
    public class GeneralBlackBoxTesting
    {        private const string SOURCE_FOLDERS = "~/testdata/DSMCBE";        //private static readonly Duplicati.Library.Main.Options opts = new Duplicati.Library.Main.Options(new Dictionary<string, string>());        private static readonly string HOME_PATH = (Environment.OSVersion.Platform == PlatformID.Unix ||                Environment.OSVersion.Platform == PlatformID.MacOSX)                ? Environment.GetEnvironmentVariable("HOME")                : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");        private static string ExpandString(string str)        {            return Environment.ExpandEnvironmentVariables(str.Replace("~", HOME_PATH));        }        private IEnumerable<string> TestFolders        {            get            {                var rx = new System.Text.RegularExpressions.Regex("r(?<number>\\d+)");                return                     from n in Directory.EnumerateDirectories(ExpandString(SOURCE_FOLDERS))                    let m = rx.Match(n)                        where m.Success                    orderby int.Parse(m.Groups["number"].Value)                    select n;            }        }        private Dictionary<string, string> TestOptions        {            get            {                return new Dictionary<string, string>();            }        }
        [Test()]
        public void TestWithSVNShort()
        {            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions);        }
    }
}

