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

using Duplicati.Library.Interface;
using Microsoft.Azure.Storage.Shared.Protocol;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.AzureBlob
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class AzureBlobBackend : IStreamingBackend
    {
        private readonly AzureBlobWrapper _azureBlob;

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public AzureBlobBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public AzureBlobBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            string storageAccountName = null;
            string accessKey = null;
            string sasToken = null;
            string containerName = uri.Host.ToLowerInvariant();

            if (options.ContainsKey("auth-username"))
                storageAccountName = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                accessKey = options["auth-password"];

            if (options.ContainsKey("azure-account-name"))
                storageAccountName = options["azure-account-name"];
            if (options.ContainsKey("azure-access-key"))
                accessKey = options["azure-access-key"];
            if (options.ContainsKey("azure-access-sas-token"))
                sasToken = options["azure-access-sas-token"];

            if (!string.IsNullOrEmpty(uri.Username))
                storageAccountName = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                accessKey = uri.Password;

            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoStorageAccountName, "AzureNoAccountName");
            }
            if (string.IsNullOrWhiteSpace(accessKey) && string.IsNullOrWhiteSpace(sasToken))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoAccessKeyOrSasToken, "AzureNoAccessKeyOrSasToken");
            }


            _azureBlob = new AzureBlobWrapper(storageAccountName, accessKey, sasToken, containerName);
        }

        public string DisplayName
        {
            get { return Strings.AzureBlobBackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "azure"; }
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken)
            => _azureBlob.ListContainerEntriesAsync(cancelToken);

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (var fs = File.Open(localname,
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await PutAsync(remotename, fs, cancelToken);
            }
        }

        public Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            return WrapWithExceptionHandler(_azureBlob.AddFileStream(remotename, input, cancelToken));
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            using (var fs = File.Open(localname,
                FileMode.Create, FileAccess.Write,
                FileShare.None))
            {
                await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.GetFileStreamAsync(remotename, output, cancellationToken));
        }

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.DeleteObjectAsync(remotename, cancellationToken));
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("azure-account-name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionShort,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionLong),
                    new CommandLineArgument("azure-access-key",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort,
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong),
                    new CommandLineArgument("azure-access-sas-token",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.SasTokenDescriptionShort,
                        Strings.AzureBlobBackend.SasTokenDescriptionLong),
                    new CommandLineArgument("auth-password",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AuthPasswordDescriptionShort,
                        Strings.AzureBlobBackend.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.AuthUsernameDescriptionShort,
                        Strings.AzureBlobBackend.AuthUsernameDescriptionLong)
                });

            }
        }

        public string Description
        {
            get
            {
                return Strings.AzureBlobBackend.Description_v2;
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(_azureBlob.DnsNames);

        public Task TestAsync(CancellationToken cancellationToken)
            => this.TestListAsync(cancellationToken);

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.AddContainerAsync(cancellationToken));
        }

        private async Task WrapWithExceptionHandler(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException e)
                when (e.RequestInformation.HttpStatusCode == 404
                || e.RequestInformation.HttpStatusMessage == StorageErrorCodeStrings.ResourceNotFound)
            {
                throw new FileMissingException(e.Message, e);
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException e)
                when (e.RequestInformation.HttpStatusMessage == StorageErrorCodeStrings.ContainerNotFound
                || e.RequestInformation.HttpStatusMessage == StorageErrorCodeStrings.ContainerBeingDeleted
                || e.RequestInformation.HttpStatusMessage == StorageErrorCodeStrings.ContainerDisabled)
            {
                throw new FolderMissingException(e.Message, e);
            }
        }

        public void Dispose()
        {

        }
    }
}
