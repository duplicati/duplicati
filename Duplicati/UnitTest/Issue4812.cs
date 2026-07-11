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

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue4812 : BasicSetupHelper
    {
        /// <summary>
        /// https://github.com/duplicati/duplicati/issues/4812
        /// `test-filters --parameters-file=...` must test the --source paths listed in the
        /// parameters file. Previously the parameters-file handling only appended --source for the
        /// `backup` command and always injected --target as the first positional argument, so for
        /// test-filters the --source entries were dropped and the --target was tested as the (bogus)
        /// source path — matching zero files.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public void TestFiltersUsesSourceFromParametersFile()
        {
            // A source folder with a known number of plain files.
            var sourceFolder = Path.Combine(DATAFOLDER, "src");
            Directory.CreateDirectory(sourceFolder);
            const int fileCount = 3;
            for (var i = 0; i < fileCount; i++)
                File.WriteAllText(Path.Combine(sourceFolder, $"file{i}.txt"), "content " + i);

            // A shared-style parameters file carrying both --target (used by backup) and --source.
            var paramFile = Path.Combine(DATAFOLDER, "params.txt");
            File.WriteAllLines(paramFile, new[]
            {
                "--source=" + sourceFolder,
                "--target=file://" + TARGETFOLDER
            });

            var sb = new StringBuilder();
            var err = new StringBuilder();
            int exit;
            using (var sw = new StringWriter(sb))
            using (var ew = new StringWriter(err))
                exit = Duplicati.CommandLine.Program.RunCommandLine(sw, ew, _ => { }, new[]
                {
                    "test-filters",
                    "--parameters-file=" + paramFile
                });

            var output = sb.ToString();
            Assert.AreEqual(0, exit, "test-filters should succeed" + Environment.NewLine + output + Environment.NewLine + err);

            var m = Regex.Match(output, @"Matched (\d+) files");
            Assert.IsTrue(m.Success, "Expected a 'Matched N files' summary" + Environment.NewLine + output);

            var matched = int.Parse(m.Groups[1].Value);
            // The --source folder's files must have been tested. Before the fix this was 0, because
            // --target was injected as the tested path and --source was ignored.
            Assert.AreEqual(fileCount, matched, "test-filters should have matched the --source files" + Environment.NewLine + output);
        }
    }
}
