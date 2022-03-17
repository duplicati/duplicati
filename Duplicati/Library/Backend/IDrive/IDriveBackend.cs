//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.IDrive
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class IDriveBackend : IBackend, IStreamingBackend
    {
        private readonly string _username = null;
        private readonly string _password = null;
        private readonly string _baseDirectoryPath = null;
        public string DisplayName => Strings.IDriveBackend.DisplayName;
        public string Description => Strings.IDriveBackend.Description;
        public string[] DNSName => null;
        public string ProtocolKey => "idrive";
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.IDriveBackend.AuthPasswordDescriptionShort, Strings.IDriveBackend.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.IDriveBackend.AuthUsernameDescriptionShort, Strings.IDriveBackend.AuthUsernameDescriptionLong)
                });
            }
        }

        private Dictionary<string, FileEntry> _fileCache;
        protected Dictionary<string, FileEntry> FileCache
        {
            get
            {
                if (_fileCache == null)
                    ResetFileCacheAsync().Wait();

                return _fileCache;
            }
            set
            {
                _fileCache = value;
            }
        }

        private IDriveApiClient _client;
        protected IDriveApiClient Client
        {
            get
            {
                if (_client == null)
                {
                    var cl = new IDriveApiClient();
                    cl.LoginAsync(_username, _password).Wait();
                    _client = cl;
                }

                return _client;
            }
        }

        public IDriveBackend()
        {
        }

        public IDriveBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url); // Sample url: idrive://Directory1/SubDirectory1?auth-username=MyUsername&auth-password=MyPassword
            _username = uri.Username;
            _password = uri.Password;

            if (string.IsNullOrEmpty(_username) && options.TryGetValue("auth-username", out string username))
                _username = username;
            if (string.IsNullOrEmpty(_password) && options.TryGetValue("auth-password", out string password))
                _password = password;

            if (string.IsNullOrEmpty(_username))
                throw new UserInformationException(Strings.IDriveBackend.NoUsernameError, "IDriveNoUsername");
            if (string.IsNullOrEmpty(_password))
                throw new UserInformationException(Strings.IDriveBackend.NoPasswordError, "IDriveNoPassword");

            _baseDirectoryPath = ("/" + (uri.HostAndPath ?? "").Trim('/') + "/").Replace("//", "/");
        }

        private async Task ResetFileCacheAsync()
        {
            FileCache = (await Client.GetFileEntryListAsync(_baseDirectoryPath))
                .Where(x => !x.IsFolder)
                .ToDictionary(x => x.Name, x => x);
        }

        public IEnumerable<IFileEntry> List()
        {
            return FileCache.Values;
        }

        public void Get(string filename, string localFilePath)
        {
            using (var fileStream = File.Create(localFilePath))
                Get(filename, fileStream);
        }

        public void Get(string filename, Stream stream)
        {
            Client.DownloadAsync(Path.Combine(_baseDirectoryPath, filename), stream).Wait();
        }

        public async Task PutAsync(string filename, string localFilePath, CancellationToken cancellationToken)
        {
            using (var fileStream = File.OpenRead(localFilePath))
                await PutAsync(filename, fileStream, cancellationToken);
        }

        public async Task PutAsync(string filename, Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                var fileEntry = await Client.UploadAsync(stream, filename, _baseDirectoryPath, cancellationToken);
                FileCache[filename] = fileEntry;
            }
            catch
            {
                FileCache = null;
                throw;
            }
        }

        public void Delete(string filename)
        {
            try
            {
                if (!FileCache.ContainsKey(filename))
                {
                    ResetFileCacheAsync().Wait();

                    if (!FileCache.ContainsKey(filename))
                        throw new FileMissingException();
                }

                Client.DeleteAsync(Path.Combine(_baseDirectoryPath, filename), _cancellationTokenSource.Token, false).Wait();

                FileCache.Remove(filename);
            }
            catch
            {
                FileCache = null;
                throw;
            }
        }

        public void CreateFolder()
        {
            var directoryParts = _baseDirectoryPath.Split('/').Where(d => !string.IsNullOrEmpty(d));
            string baseDirectory = "/";

            foreach (string directoryPart in directoryParts)
            {
                Client.CreateDirectoryAsync(directoryPart, baseDirectory, _cancellationTokenSource.Token).Wait();
                baseDirectory += directoryPart + "/";
            }
        }

        public void Test()
        {
            this.TestList();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
