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

using System.IO;
using NUnit.Framework;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.DynamicLoader;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Duplicati.UnitTest
{
    public class Issue5862 : BasicSetupHelper
    {
        private class PreventingBackend : IStreamingBackend
        {
            public const string Key = "test-backend-5862";
            public string DisplayName => "Test backend";
            public string ProtocolKey => Key;
            public string Description => "Test backend";
            public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;
            private readonly IStreamingBackend backend;

            public static string RealKey { get; set; } = "file";
            public PreventingBackend()
            {
                backend = (IStreamingBackend)BackendLoader.Backends.First(x => x.ProtocolKey == RealKey);
            }

            public PreventingBackend(string url, Dictionary<string, string> opts)
            {
                backend = (IStreamingBackend)BackendLoader.GetBackend(RealKey + url.Substring(ProtocolKey.Length), opts);
            }

            public Task CreateFolderAsync(CancellationToken cancellationToken)
                => backend.CreateFolderAsync(cancellationToken);

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
                => backend.DeleteAsync(remotename, cancellationToken);
            public void Dispose()
                => backend.Dispose();
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => backend.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.GetAsync(remotename, filename, cancellationToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
                => GetDNSNamesAsync(cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
                => backend.ListAsync(cancellationToken);

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => throw new DeterministicErrorBackend.DeterministicErrorBackendException("Prevent");

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
                => throw new DeterministicErrorBackend.DeterministicErrorBackendException("Prevent");

            public Task TestAsync(CancellationToken cancellationToken)
                => backend.TestAsync(cancellationToken);
        }

        [Test]
        [Category("Targeted")]
        public void TestUploadFailureWithResume()
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,
            });

            BackendLoader.AddBackend(new PreventingBackend());
            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            // Make a failing backup
            using (var c = new Library.Main.Controller(PreventingBackend.Key + "://" + TARGETFOLDER, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            // Make a working backup, should not give any errors
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));
        }
    }
}

