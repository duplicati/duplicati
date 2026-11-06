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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The backend manager stops its queue runner when an operation ends, and the runner disposes the
    /// backends it pooled. A backend that takes a while to close, such as one that has to say goodbye
    /// to a server, must not turn that into a warning on the operation.
    /// </summary>
    public class BackendManagerShutdownTests : BasicSetupHelper
    {
        /// <summary>
        /// A file backend whose dispose takes longer than a second
        /// </summary>
        private sealed class SlowDisposeBackend : DeterministicErrorBackend, IBackend
        {
            public SlowDisposeBackend()
            {
            }

            public SlowDisposeBackend(string url, Dictionary<string, string> options)
                : base(url, options)
            {
            }

            string IBackend.ProtocolKey => "slowdispose";

            void IDisposable.Dispose()
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(1500));
                Dispose();
            }
        }

        [Test]
        [Category("Targeted")]
        public async Task ABackendThatIsSlowToCloseGivesNoWarningAsync()
        {
            Library.DynamicLoader.BackendLoader.AddBackend(new SlowDisposeBackend());
            Directory.CreateDirectory(DATAFOLDER);
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");

            using var c = new Controller("slowdispose://" + TARGETFOLDER, TestOptions, null);
            TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));
        }
    }
}
