#nullable enable

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Implements a destination where data can be restored to
    /// </summary>
    public interface IDestinationProvider : IDynamicModule
    {
        /// <summary>
        /// Gets the module key
        /// </summary>
        string Key { get; }
        /// <summary>
        /// Gets the display name of the module
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Checks if a restore is needed
        /// </summary>
        /// <param name="path">The path to restore to</param>
        /// <param name="dataHash">The hash of the data to restore</param>
        /// <param name="metadataHash">The hash of the metadata to restore</param>
        /// <param name="cancel">The cancellation token</param>
        /// <returns>True if a restore is needed, false otherwise</returns>
        Task<bool> IsRestoreNeededAsync(string path, string dataHash, string? metadataHash, CancellationToken cancel);

        /// <summary>
        /// Restores a file to the given path
        /// </summary>
        /// <param name="path">The path to restore to</param>
        /// <param name="data">The data stream to restore</param>
        /// <param name="metadata">The metadata stream to restore</param>
        /// <param name="cancel">The cancellation token</param>
        /// <returns>A task that completes when the restore is done</returns>
        Task WriteAsync(string path, Stream data, Stream? metadata, CancellationToken cancel);
    }
}