// Copyright (C) 2024, The Duplicati Team
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

            if (options.ContainsKey("azure_account_name"))
                storageAccountName = options["azure_account_name"];
            if (options.ContainsKey("azure-account-name"))
                storageAccountName = options["azure-account-name"];

            if (options.ContainsKey("azure_access_key"))
                accessKey = options["azure_access_key"];
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

        public IEnumerable<IFileEntry> List()
        {
            return _azureBlob.ListContainerEntries();
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (var fs = File.Open(localname,
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await PutAsync(remotename, fs, cancelToken);
            }
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            await _azureBlob.AddFileStream(remotename, input, cancelToken);
        }

        public void Get(string remotename, string localname)
        {
            using (var fs = File.Open(localname,
                FileMode.Create, FileAccess.Write,
                FileShare.None))
            {
                Get(remotename, fs);
            }
        }

        public void Get(string remotename, Stream output)
        {
            _azureBlob.GetFileStream(remotename, output);
        }

        public void Delete(string remotename)
        {
            _azureBlob.DeleteObject(remotename);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("azure_account_name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionShort,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionLong,
                        null, null, null, "This is deprecated, use azure-account-name instead"),
                    new CommandLineArgument("azure_access_key",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort,
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong,
                        null, null, null, "This is deprecated, use azure-access-key instead"),
                    new CommandLineArgument("azure_blob_container_name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.ContainerNameDescriptionShort,
                        Strings.AzureBlobBackend.ContainerNameDescriptionLong,
                        null, null, null, "This is deprecated, use azure-blob-container-name instead"),
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
                    new CommandLineArgument("azure-blob-container-name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.ContainerNameDescriptionShort,
                        Strings.AzureBlobBackend.ContainerNameDescriptionLong),
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

        public string[] DNSName
        {
            get { return _azureBlob.DnsNames; }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            _azureBlob.AddContainer();
        }

        public void Dispose()
        {

        }
    }
}
