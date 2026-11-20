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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.IPC;
using Duplicati.Library.Main.IPC.Dto;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A repair reports the state of the destination, as a backup does, so the server can update the
    /// number of versions it shows for the job (#3114).
    /// </summary>
    public class RepairStatisticsTests : BasicSetupHelper
    {
        private async Task<Dictionary<string, string>> MakeThreeVersionsAsync()
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            for (var i = 0; i < 3; i++)
            {
                File.WriteAllText(Path.Combine(DATAFOLDER, $"file{i}.txt"), $"version {i}");
                using var c = new Controller("file://" + TARGETFOLDER, options, null);
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));
            }
            return options;
        }

        private static IParsedBackendStatistics Statistics(IRepairResults results)
            => (results as IBackendStatsticsReporter)?.BackendStatistics as IParsedBackendStatistics;

        [Test]
        [Category("Repair")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task ARepairReportsTheVersionsAtTheDestination_Async(bool recreate)
        {
            var options = await MakeThreeVersionsAsync().ConfigureAwait(false);
            if (recreate)
                File.Delete(DBFILE);

            IRepairResults results;
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                results = await c.RepairAsync().ConfigureAwait(false);
            TestUtils.AssertResults(results);

            var statistics = Statistics(results);
            Assert.That(statistics, Is.Not.Null, "The repair should report the state of the destination");
            Assert.That(statistics.BackupListCount, Is.EqualTo(3));
            Assert.That(statistics.KnownFilesets, Is.EqualTo(3));
            Assert.That(statistics.KnownFileCount, Is.EqualTo(Directory.GetFiles(TARGETFOLDER).Length));
            Assert.That(statistics.RemoteCalls, Is.GreaterThan(0));

            // The server may run the operation in another process and get the results through a DTO
            var wrapped = new RepairResultsWrapper(RepairResultsDto.FromResults(results));
            Assert.That(Statistics(wrapped)?.BackupListCount, Is.EqualTo(3), "The statistics should reach the server from the controller process");
        }
    }
}
