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

        public AzureBlobWrapper(string accountName, string accessKey, string containerName)
        {
            _containerName = containerName;
            var connectionString = string.Format("DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}",
                accountName, accessKey);
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            _container = blobClient.GetContainerReference(containerName);
        }

        public void AddContainer()
        {
            _container.CreateAsync().GetAwaiter().GetResult();
            _container.SetPermissionsAsync(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Off });
        }

        public virtual void GetFileStream(string keyName, Stream target)
        {
            _container.GetBlockBlobReference(keyName).DownloadToStreamAsync(target).GetAwaiter().GetResult();
        }

        public void GetFileObject(string keyName, string localfile)
        {
            _container.GetBlockBlobReference(keyName).DownloadToFileAsync(localfile, FileMode.Create).GetAwaiter().GetResult();
        }

        public void AddFileObject(string keyName, string localfile)
        {
            using (var fs = File.Open(localfile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                AddFileStream(keyName, fs);
            }
        }

        public virtual void AddFileStream(string keyName, Stream source)
        {
            _container.GetBlockBlobReference(keyName).UploadFromStreamAsync(source).GetAwaiter().GetResult();
        }

        public void DeleteObject(string keyName)
        {
            _container.GetBlockBlobReference(keyName).DeleteIfExistsAsync().GetAwaiter().GetResult();
        }

        private IEnumerable<IListBlobItem> ListBlobs(){
            BlobContinuationToken continuationToken = null;
            var results = new List<IListBlobItem>();

            do
            {
                var response = _container.ListBlobsSegmentedAsync("", true, BlobListingDetails.Metadata, null, continuationToken, null, null).GetAwaiter().GetResult();
                continuationToken = response.ContinuationToken;
                results.AddRange(response.Results);
            }
            while (continuationToken != null);

            return results;
        }

        public virtual List<IFileEntry> ListContainerEntries()
        {
            var listBlobItems = ListBlobs();
            try
            {
                return listBlobItems.Select(x =>
                {
                    var absolutePath = x.StorageUri.PrimaryUri.AbsolutePath;
                    var containerSegment = string.Concat("/", _containerName, "/");
                    var blobName = absolutePath.Substring(absolutePath.IndexOf(
                        containerSegment, System.StringComparison.Ordinal) + containerSegment.Length);

                    try
                    {
                        if (x is CloudBlockBlob)
                        {
                            var cb = (CloudBlockBlob)x;
                            var modified = cb.Properties.LastModified;
                            var lastModified = new System.DateTime();
                            if (cb.Properties.LastModified != null)
                                lastModified = new System.DateTime(cb.Properties.LastModified.Value.Ticks, System.DateTimeKind.Utc);
                            return new FileEntry(Uri.UrlDecode(blobName.Replace("+", "%2B")), cb.Properties.Length, lastModified, lastModified);
                        }
                    }
                    catch
                    { 
                        // If the metadata fails to parse, return the basic entry
                    }

                    return new FileEntry(Uri.UrlDecode(blobName.Replace("+", "%2B")));
                })
                .Cast<IFileEntry>()
                .ToList();
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 404)
                {
                    throw new FolderMissingException(ex);
                }
                throw;
            }
        }
    }
}
