#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Duplicati.Library.Backend.AzureBlob
{
    /// <summary>
    /// Azure blob storage facade.
    /// </summary>
    public class AzureBlobWrapper
    {
        private readonly string _containerName;
        private readonly CloudBlobContainer _container;

        public string[] DnsNames
        {
            get
            {
                var lst = new List<string>();
                if (_container != null)
                {
                    if (_container.Uri != null)
                        lst.Add(_container.Uri.Host);

                    if (_container.StorageUri != null)
                    {
                        if (_container.StorageUri.PrimaryUri != null)
                            lst.Add(_container.StorageUri.PrimaryUri.Host);
                        if (_container.StorageUri.SecondaryUri != null)
                            lst.Add(_container.StorageUri.SecondaryUri.Host);
                    }
                }

                return lst.ToArray();
            }
        }

        public AzureBlobWrapper(string accountName, string accessKey, string sasToken, string containerName)
        {
            OperationContext.GlobalSendingRequest += (sender, args) =>
            {
                args.Request.UserAgent = string.Format(
                    "APN/1.0 Duplicati/{0} AzureBlob/2.0 {1}",
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version,
                    Microsoft.WindowsAzure.Storage.Shared.Protocol.Constants.HeaderConstants.UserAgent
                );
            };

            string connectionString;
            if (sasToken != null)
            {
                connectionString = string.Format("DefaultEndpointsProtocol=https;AccountName={0};SharedAccessSignature={1}",
                    accountName, sasToken);
            }
            else
            {
                connectionString = string.Format("DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}",
                            accountName, accessKey);
            }

            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();

            _containerName = containerName;
            _container = blobClient.GetContainerReference(containerName);
        }

        public void AddContainer()
        {
            _container.Create(BlobContainerPublicAccessType.Off);
        }

        public virtual void GetFileStream(string keyName, Stream target)
        {
            _container.GetBlockBlobReference(keyName).DownloadToStream(target);
        }

        public virtual async Task AddFileStream(string keyName, Stream source, CancellationToken cancelToken)
        {
            await _container.GetBlockBlobReference(keyName).UploadFromStreamAsync(source, cancelToken);
        }

        public void DeleteObject(string keyName)
        {
            _container.GetBlockBlobReference(keyName).DeleteIfExists();
        }

        public virtual IEnumerable<IFileEntry> ListContainerEntries(string prefix = null)
        {
            var listBlobItems = _container.ListBlobs(prefix: prefix, blobListingDetails: BlobListingDetails.Metadata);

            IFileEntry fileEntry = null;

            foreach (var listBlobItem in listBlobItems)
            {
                try
                {
                    var absolutePath = listBlobItem.StorageUri.PrimaryUri.AbsolutePath;
                    var containerSegment = string.Concat("/", _containerName, "/");
                    var blobName = absolutePath.Substring(absolutePath.IndexOf(
                        containerSegment, System.StringComparison.Ordinal) + containerSegment.Length);

                    try
                    {
                        if (listBlobItem is CloudBlockBlob cb)
                        {
                            var lastModified = new System.DateTime();
                            if (cb.Properties.LastModified != null)
                                lastModified = new System.DateTime(cb.Properties.LastModified.Value.Ticks, System.DateTimeKind.Utc);
                            fileEntry = new FileEntry(Uri.UrlDecode(blobName.Replace("+", "%2B")), cb.Properties.Length, lastModified, lastModified);
                        }
                    }
                    catch
                    {
                        // If the metadata fails to parse, return the basic entry
                        fileEntry = new FileEntry(Uri.UrlDecode(blobName.Replace("+", "%2B")));
                    }
                }
                catch (StorageException ex)
                {
                    if (ex.RequestInformation.HttpStatusCode == 404)
                    {
                        throw new FolderMissingException(Strings.AzureBlobBackend.MissingContainerError(_containerName, ex.Message), ex);
                    }
                    throw;
                }
                yield return fileEntry;
            }

        }
    }
}
