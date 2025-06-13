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

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
	public class Issue1723 : BasicSetupHelper
	{
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
				Assert.AreEqual(0, r.Errors.Count());
				Assert.AreEqual(0, r.Warnings.Count());
				var pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
				if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
					throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));
			}


		}
	}
}
