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

using System;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.AzureBlob
{
    public class AzureBlobBackend : IStreamingBackend
    {
        private readonly AzureBlobWrapper _azureBlob;

        public AzureBlobBackend()
        {
        }

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
            if (options.ContainsKey("azure_account_name"))
                storageAccountName = options["azure_account_name"];
            if (options.ContainsKey("azure_access_key"))
                accessKey = options["azure_access_key"];
            if (!string.IsNullOrEmpty(uri.Username))
                storageAccountName = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                accessKey = uri.Password;

            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoStorageAccountName);
            }
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                throw new UserInformationException(Strings.AzureBlobBackend.NoAccessKey);
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

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public List<IFileEntry> List()
        {
            return _azureBlob.ListContainerEntries();
        }

        public void Put(string remotename, string localname)
        {
            using (var fs = File.Open(localname,
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Put(remotename, fs);
            }
        }

        public void Put(string remotename, Stream input)
        {
            _azureBlob.AddFileStream(remotename, input);
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
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionLong),
                    new CommandLineArgument("azure_access_key", 
                        CommandLineArgument.ArgumentType.Password, 
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort, 
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong),
                    new CommandLineArgument("azure_blob_container_name", 
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

        public void Test()
        {
            List();
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
