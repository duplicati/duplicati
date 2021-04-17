using System.Collections.Generic;
using System.Threading;
namespace Duplicati.Library.Interface
{
    /// <summary>
    /// A backend interface that adds support for long file listings by allowing each item to be fetched in turn
    /// </summary>
    public interface IBackendPagination : IBackend
    {
        /// <summary>
        /// Enumerates a list of files found on the remote location
        /// </summary>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>The list of files</returns>
        IAsyncEnumerable<IFileEntry> ListEnumerableAsync(CancellationToken cancelToken);
    }
}