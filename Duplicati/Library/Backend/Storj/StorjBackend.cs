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
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using uplink.NET.Interfaces;
using uplink.NET.Models;
using uplink.NET.Services;

namespace Duplicati.Library.Backend.Storj
{
    public class Storj : IStreamingBackend
    {
        private const string STORJ_AUTH_METHOD = "storj-auth-method";
        private const string STORJ_SATELLITE = "storj-satellite";
        private const string STORJ_API_KEY = "storj-api-key";
        private const string STORJ_SECRET = "storj-secret";
        private const string STORJ_SHARED_ACCESS = "storj-shared-access";
        private const string STORJ_BUCKET = "storj-bucket";
        private const string STORJ_FOLDER = "storj-folder";

        private const string PROTOCOL_KEY = "storj";
        private const string STORJ_PARTNER_ID = "duplicati";

        private const string STORJ_AUTH_METHOD_API_KEY = "API key";
        private const string STORJ_AUTH_METHOD_ACCESS_GRANT = "Access grant";

        private const string STORJ_DEFAULT_BUCKET = "duplicati";
        private const string STORJ_DEFAULT_SATELLITE = "us1.storj.io:7777";
        private const string STORJ_DEFAULT_AUTH_METHOD = STORJ_AUTH_METHOD_API_KEY;

        private readonly string _satellite;
        private readonly string _api_key;
        private readonly string _secret;
        private readonly string _bucket;
        private readonly string _folder;
        private Access _access;
        private IBucketService _bucketService;
        private IObjectService _objectService;

        public static readonly Dictionary<string, string> KNOWN_STORJ_SATELLITES = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "US Central", "us1.storj.io:7777" },
            { "Asia East", "ap1.storj.io:7777" },
            { "Europe", "eu1.storj.io:7777" },
        };

        public static readonly Dictionary<string, string> KNOWN_AUTHENTICATION_METHODS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "API key", "API key" },
            { "Access grant", "Access grant" },
        };

        private static bool _libraryLoaded = false;
        private static void InitStorjLibrary()
        {
            if (_libraryLoaded)
                return;

            Access.SetTempDirectory(Library.Utility.TempFolder.SystemTempPath);
            _libraryLoaded = true;
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Storj()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Storj(string url, Dictionary<string, string> options)
        {
            InitStorjLibrary();

            var auth_method = options.GetValueOrDefault(STORJ_AUTH_METHOD, STORJ_DEFAULT_AUTH_METHOD);
            if (string.Equals(auth_method, STORJ_AUTH_METHOD_ACCESS_GRANT, StringComparison.OrdinalIgnoreCase))
            {
                //Create an access from the access grant
                var shared_access = options[STORJ_SHARED_ACCESS];
                _access = new Access(shared_access, new Config() { UserAgent = STORJ_PARTNER_ID });
            }
            else
            {
                //Create an access for a satellite, API key and encryption passphrase                    
                _satellite = options.GetValueOrDefault(STORJ_SATELLITE, STORJ_DEFAULT_SATELLITE);

                if (options.ContainsKey(STORJ_API_KEY))
                {
                    _api_key = options[STORJ_API_KEY];
                }
                if (options.ContainsKey(STORJ_SECRET))
                {
                    _secret = options[STORJ_SECRET];
                }

                _access = new Access(_satellite, _api_key, _secret, new Config() { UserAgent = STORJ_PARTNER_ID });
            }

            _bucketService = new BucketService(_access);
            _objectService = new ObjectService(_access);

            //If no bucket was provided use the default "duplicati"-bucket
            if (options.ContainsKey(STORJ_BUCKET))
            {
                _bucket = options[STORJ_BUCKET];
            }
            else
            {
                _bucket = "duplicati";
            }

            if (options.ContainsKey(STORJ_FOLDER))
            {
                _folder = options[STORJ_FOLDER];
            }
        }

        public string DisplayName
        {
            get { return Strings.Storj.DisplayName; }
        }

        public string ProtocolKey => PROTOCOL_KEY;

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(STORJ_AUTH_METHOD, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAuthMethodDescriptionShort, Strings.Storj.StorjAuthMethodDescriptionLong, STORJ_DEFAULT_AUTH_METHOD, null, [STORJ_AUTH_METHOD_API_KEY, STORJ_AUTH_METHOD_ACCESS_GRANT]),
                    new CommandLineArgument(STORJ_SATELLITE, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSatelliteDescriptionShort, Strings.Storj.StorjSatelliteDescriptionLong, STORJ_DEFAULT_SATELLITE),
                    new CommandLineArgument(STORJ_API_KEY, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAPIKeyDescriptionShort, Strings.Storj.StorjAPIKeyDescriptionLong),
                    new CommandLineArgument(STORJ_SECRET, CommandLineArgument.ArgumentType.Password, Strings.Storj.StorjSecretDescriptionShort, Strings.Storj.StorjSecretDescriptionLong),
                    new CommandLineArgument(STORJ_SHARED_ACCESS, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSharedAccessDescriptionShort, Strings.Storj.StorjSharedAccessDescriptionLong),
                    new CommandLineArgument(STORJ_BUCKET, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjBucketDescriptionShort, Strings.Storj.StorjBucketDescriptionLong, STORJ_DEFAULT_BUCKET),
                    new CommandLineArgument(STORJ_FOLDER, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjFolderDescriptionShort, Strings.Storj.StorjFolderDescriptionLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.Storj.Description;
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            //Storj DCS has no folders
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
                await _objectService.DeleteObjectAsync(bucket, GetBasePath() + remotename).ConfigureAwait(false);
            }
            catch (Exception root)
            {
                throw new FileMissingException(root);
            }
        }

        public void Dispose()
        {
            if (_objectService != null)
            {
                _objectService = null;
            }
            if (_bucketService != null)
            {
                _bucketService = null;
            }
            if (_access != null)
            {
                _access.Dispose();
                _access = null;
            }
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
            var download = await _objectService.DownloadObjectAsync(bucket, GetBasePath() + remotename, new DownloadOptions(), false).ConfigureAwait(false);
            await download.StartDownloadAsync().ConfigureAwait(false);

            if (download.Completed)
            {
                using (var file = new FileStream(filename, FileMode.Create))
                {
                    await file.WriteAsync(download.DownloadedBytes, 0, (int)download.BytesReceived, cancelToken).ConfigureAwait(false);
                    await file.FlushAsync(cancelToken).ConfigureAwait(false);
                }
            }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            int index = 0;
            var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
            var download = await _objectService.DownloadObjectAsync(bucket, GetBasePath() + remotename, new DownloadOptions(), false).ConfigureAwait(false);
            download.DownloadOperationProgressChanged += (op) =>
            {
                cancelToken.ThrowIfCancellationRequested();
                int newPartLength = (int)op.BytesReceived - index;
                byte[] newPart = new byte[newPartLength];
                Array.Copy(op.DownloadedBytes, index, newPart, 0, newPartLength);
                stream.Write(newPart, 0, newPartLength);
                index = index + newPartLength;
            };
            await download.StartDownloadAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
            var prefix = GetBasePath();
            var objects = await _objectService.ListObjectsAsync(bucket, new ListObjectsOptions { Recursive = true, System = true, Custom = true, Prefix = prefix }).ConfigureAwait(false);

            foreach (var obj in objects.Items)
            {
                var file = new StorjFile(obj);
                if (prefix != "")
                {
                    file.Name = file.Name.Replace(prefix, "");
                }
                yield return file;
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
            CustomMetadata custom = new CustomMetadata();
            custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_ACCESS, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_MODIFICATION, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            var upload = await _objectService.UploadObjectAsync(bucket, GetBasePath() + remotename, new UploadOptions(), stream, custom, false).ConfigureAwait(false);
            await upload.StartUploadAsync().ConfigureAwait(false);
            if (upload.Failed)
            {
                throw new Exception(upload.ErrorMessage);
            }
        }

        public async Task TestAsync(CancellationToken cancelToken)
        {
            var ct = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            var testTask = TestImplAsync(ct.Token);
            ct.CancelAfter(10000);
            await Task.WhenAny(testTask, Task.Delay(Timeout.Infinite, ct.Token)).ConfigureAwait(false);
            if (testTask.IsCompleted)
                await testTask.ConfigureAwait(false);
            else if (!testTask.IsCompletedSuccessfully)
                throw new Exception(Strings.Storj.TestConnectionFailed);
        }

        /// <summary>
        /// Test the connection by:
        /// - creating the bucket (if it not already exists)
        /// - uploading 256 random bytes to a test-file
        /// - downloading the file back and expecting 256 bytes
        /// </summary>
        /// <returns>true, if the test was successfull or and exception</returns>
        private async Task<bool> TestImplAsync(CancellationToken cancelToken)
        {
            string testFileName = GetBasePath() + "duplicati_test.dat";

            var bucket = await _bucketService.EnsureBucketAsync(_bucket).ConfigureAwait(false);
            var upload = await _objectService.UploadObjectAsync(bucket, testFileName, new UploadOptions(), GetRandomBytes(256), false).ConfigureAwait(false);
            await upload.StartUploadAsync().ConfigureAwait(false);

            var download = await _objectService.DownloadObjectAsync(bucket, testFileName, new DownloadOptions(), false).ConfigureAwait(false);
            await download.StartDownloadAsync().ConfigureAwait(false);

            await _objectService.DeleteObjectAsync(bucket, testFileName).ConfigureAwait(false);

            if (download.Failed || download.BytesReceived != 256)
            {
                throw new Exception(download.ErrorMessage);
            }

            return true;
        }

        /// <summary>
        /// Gets the base path - depending on there is a folder set or not
        /// </summary>
        /// <returns>The base path within a bucket where the backup shall be placed</returns>
        private string GetBasePath()
        {
            if (!string.IsNullOrEmpty(_folder))
                return _folder + "/";
            else
                return "";
        }

        /// <summary>
        /// Creates some random bytes with the given length - just for testing the connection
        /// </summary>
        /// <param name="length">The length of the bytes to create</param>
        /// <returns>A byte-array with the given length</returns>
        private static byte[] GetRandomBytes(long length)
        {
            byte[] bytes = new byte[length];
            Random rand = new Random();
            rand.NextBytes(bytes);

            return bytes;
        }
    }
}
