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
            string containerName = uri.Host.ToLowerInvariant();

            if (options.ContainsKey("auth-username"))
                storageAccountName = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                accessKey = options["auth-password"];
            if (options.ContainsKey("azure_account_name") || options.ContainsKey("azure-account-name"))
                storageAccountName = options["azure_account_name"] ?? options["azure-account-name"];
            if (options.ContainsKey("azure_access_key") || options.ContainsKey("azure-access-key"))
                accessKey = options["azure_access_key"] ?? options["azure-access-key"];
            if (!string.IsNullOrEmpty(uri.Username))
                storageAccountName = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                accessKey = uri.Password;

            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoStorageAccountName, "AzureNoAccountName");
            }
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoAccessKey, "AzureNoAccessKey");
            }

            _azureBlob = new AzureBlobWrapper(storageAccountName, accessKey, containerName);
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

        public Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (var fs = File.Open(localname,
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return PutAsync(remotename, fs, cancelToken);
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
                        null, null, null, "This is deprecated, use azure-account-name insted"),
                    new CommandLineArgument("azure_access_key",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort,
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong,
                        null, null, null, "This is deprecated, use azure-access-key insted"),
                    new CommandLineArgument("azure_blob_container_name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.ContainerNameDescriptionShort,
                        Strings.AzureBlobBackend.ContainerNameDescriptionLong,
                        null, null, null, "This is deprecated, use azure-blob-container-name insted"),
                    new CommandLineArgument("azure-account-name",
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionShort,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionLong),
                    new CommandLineArgument("azure-access-key",
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort,
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong),
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
