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

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Contains extension methods for the IBackend interfaces
    /// </summary>
    public static class BackendExtensions
    {
        /// <summary>
        /// Tests a backend by invoking the List() method.
        /// As long as the iteration can either complete or find at least one file without throwing, the test is successful
        /// </summary>
        /// <param name="backend">Backend to test</param>
        /// <param name="cancelToken">Cancellation token</param>
        public static async Task TestListAsync(this IBackend backend, CancellationToken cancelToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            // If we can iterate successfully, even if it's empty, then the backend test is successful
            await foreach (IFileEntry file in backend.ListAsync(cts.Token).ConfigureAwait(false))
            {
                cts.Cancel();
                return;
            }
        }
    }
}
