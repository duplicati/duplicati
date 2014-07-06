

#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
            //var uri = new Utility.Uri(url);
            //uri.RequireHost();

            if (!options.ContainsKey("azure_blob_connection_string"))
            {
                throw new Exception(Strings.AzureBlobBackend.NoStorageConnectionString);
            }

            if (!options.ContainsKey("azure_blob_container_name"))
            {
                throw new Exception(Strings.AzureBlobBackend.NoContainerName);
            }

            _azureBlob = new AzureBlobWrapper(options["azure_blob_connection_string"],
                options["azure_blob_container_name"].ToLowerInvariant());
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
                    new CommandLineArgument("azure_blob_connection_string", 
                        CommandLineArgument.ArgumentType.Password, 
                        Strings.AzureBlobBackend.StorageConnectionStringDescriptionShort, 
                        Strings.AzureBlobBackend.StorageConnectionStringDescriptionLong),
                    new CommandLineArgument("azure_blob_container_name", 
                        CommandLineArgument.ArgumentType.String, 
                        Strings.AzureBlobBackend.ContainerNameDescriptionShort, 
                        Strings.AzureBlobBackend.ContainerNameDescriptionLong)
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
