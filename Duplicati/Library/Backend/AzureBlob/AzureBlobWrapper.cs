using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.AzureBlob
{
    /// <summary>
    /// Azure blob storage facade using modern Azure.Storage.Blobs SDK.
    /// </summary>
    public sealed class AzureBlobWrapper
    {
        private readonly BlobContainerClient _container;

        /// <summary>
        /// Gets an array of DNS names associated with the blob container.
        /// </summary>
        /// <returns>An array of DNS hostnames for the primary and secondary URIs of the container.</returns>
        public string[] DnsNames
        {
            get
            {
                var lst = new List<string>();
                if (_container != null && _container.Uri != null) lst.Add(_container.Uri.Host);
                return lst.ToArray();
            }
        }

        /// <summary>
        /// Initializes a new instance of the AzureBlobWrapper class.
        /// </summary>
        /// <param name="accountName">The Azure storage account name.</param>
        /// <param name="accessKey">The access key for the storage account.</param>
        /// <param name="sasToken">The Shared Access Signature (SAS) token for authentication.</param>
        /// <param name="containerName">The name of the blob container.</param>
        public AzureBlobWrapper(string accountName, string accessKey, string sasToken, string containerName)
        {
            BlobServiceClient blobServiceClient;
            if (sasToken != null)
            {
                var sasUri = new Uri($"https://{accountName}.blob.core.windows.net/?{sasToken}");
                blobServiceClient = new BlobServiceClient(sasUri);
            }
            else
            {
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accessKey};EndpointSuffix=core.windows.net";
                blobServiceClient = new BlobServiceClient(connectionString);
            }

            _container = blobServiceClient.GetBlobContainerClient(containerName);
        }

        /// <summary>
        /// Creates a new blob container asynchronously and sets its access permissions to private.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task AddContainerAsync(CancellationToken cancellationToken)
        {
            await _container.CreateAsync(PublicAccessType.None, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a blob to a stream asynchronously.
        /// </summary>
        /// <param name="keyName">The name of the blob to download.</param>
        /// <param name="target">The stream to download the blob to.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        public async Task GetFileStreamAsync(string keyName, Stream target, CancellationToken cancellationToken)
        {
            var blobClient = _container.GetBlobClient(keyName);
            await blobClient.DownloadToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a stream to a blob asynchronously.
        /// </summary>
        /// <param name="keyName">The name to give the uploaded blob.</param>
        /// <param name="source">The stream containing the data to upload.</param>
        /// <param name="cancelToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous upload operation.</returns>
        public async Task AddFileStream(string keyName, Stream source, CancellationToken cancelToken)
        {
            var blobClient = _container.GetBlobClient(keyName);
            await blobClient.UploadAsync(source, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a blob if it exists asynchronously.
        /// </summary>
        /// <param name="keyName">The name of the blob to delete.</param>
        /// <param name="cancelToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        public async Task DeleteObjectAsync(string keyName, CancellationToken cancelToken)
        {
            var blobClient = _container.GetBlobClient(keyName);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists all files in the container and returns them as IFileEntry objects.
        /// </summary>
        /// <returns>A list of file entries representing the blobs in the container.</returns>
        /// <exception cref="FolderMissingException">Thrown when the container does not exist.</exception>
        public List<IFileEntry> ListContainerEntries()
        {
            try
            {
                var blobs = _container.GetBlobs();
                return blobs.Select(blobItem =>
                {
                    var blobName = Uri.UnescapeDataString(blobItem.Name.Replace("+", "%2B"));
                    
                    if (blobItem is { } bi)
                    {
                        var lastModified = bi.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow;
                        return new FileEntry(blobName, bi.Properties.ContentLength ?? 0, lastModified, lastModified);
                    }

                    return new FileEntry(blobName);
                })
                .Cast<IFileEntry>()
                .ToList();
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                throw new FolderMissingException(ex);
            }
        }
    }
}