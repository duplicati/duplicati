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
using System;
using System.IO;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
	public class Issue1723 : BasicSetupHelper
	{
        [OneTimeSetUp]
		public override void PrepareSourceData()
		{
			base.PrepareSourceData();

			Directory.CreateDirectory(DATAFOLDER);
			Directory.CreateDirectory(TARGETFOLDER);
		}

		[Test]
        [Category("Targeted")]
		public void RunCommands()
		{
			var testopts = TestOptions;
			testopts["no-backend-verification"] = "true";

			var data = new byte[1024 * 1024 * 10];
			File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
			using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
			{
				var r = c.Backup(new string[] { DATAFOLDER });
				var pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
				if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
					throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));
			}


		}
	}
}
