using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Contains extension methods for the IBackend interfaces
    /// </summary>
    public static class BackendExtensions
    {
        /// <summary>
        /// Tests a backend by invoking the ListAsync() method.
        /// As long as the iteration can either complete or find at least one file without throwing, the test is successful
        /// </summary>
        /// <param name="backend">Backend to test</param>
        /// <param name="token">The cancellation token to use</param>
        /// <returns>An awaitable task</returns>
        public static async Task TestListAsync(this IBackend backend, CancellationToken token)
        {
            if (backend is IBackendPagination backendPagination)
            {
                await foreach(var res in backendPagination.ListEnumerableAsync(token))
                    break;
            }
            else
            {
            // If we can iterate successfully, even if it's empty, then the backend test is successful
                foreach(var res in await backend.ListAsync(token))
                    break;
            }
        }

        /// <summary>
        /// Converts a paginated list into a condensed simple list
        /// </summary>
        /// <param name="backend">The pagination enabled backend</param>
        /// <param name="token">The cancellation token to use</param>
        /// <returns>The complete list</returns>
        public static async Task<IList<IFileEntry>> CondensePaginatedListAsync(this IBackendPagination backend, CancellationToken token)
        {
            var lst = new List<IFileEntry>();
            await foreach(var n in backend.ListEnumerableAsync(token))
                lst.Add(n);

            return lst;
        }
    }
}
