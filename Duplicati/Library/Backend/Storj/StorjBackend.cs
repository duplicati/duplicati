using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
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

        [DllImport("kernel32.dll")]
        protected static extern IntPtr LoadLibrary(string filename);

        private static bool _libraryLoaded = false;
        private static void InitStorjLibrary()
        {
            if (_libraryLoaded)
                return;

            if (Duplicati.Library.Common.Platform.IsClientWindows) //We need to init only on Windows to distinguish between x64 and x86
            {
                if (System.Environment.Is64BitProcess)
                {
                    var res = LoadLibrary("win-x64/storj_uplink.dll");
                }
                else
                {
                    var res = LoadLibrary("win-x86/storj_uplink.dll");
                }
            }
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

            var auth_method = options[STORJ_AUTH_METHOD];
            if (auth_method == "Access grant")
            {
                //Create an access from the access grant
                var shared_access = options[STORJ_SHARED_ACCESS];
                _access = new Access(shared_access, new Config() { UserAgent = STORJ_PARTNER_ID });
            }
            else
            {
                //Create an access for a satellite, API key and encryption passphrase
                _satellite = options[STORJ_SATELLITE];

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
                    new CommandLineArgument(STORJ_AUTH_METHOD, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAuthMethodDescriptionShort, Strings.Storj.StorjAuthMethodDescriptionLong, "API key"),
                    new CommandLineArgument(STORJ_SATELLITE, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSatelliteDescriptionShort, Strings.Storj.StorjSatelliteDescriptionLong, "us1.storj.io:7777"),
                    new CommandLineArgument(STORJ_API_KEY, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAPIKeyDescriptionShort, Strings.Storj.StorjAPIKeyDescriptionLong),
                    new CommandLineArgument(STORJ_SECRET, CommandLineArgument.ArgumentType.Password, Strings.Storj.StorjSecretDescriptionShort, Strings.Storj.StorjSecretDescriptionLong),
                    new CommandLineArgument(STORJ_SHARED_ACCESS, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSharedAccessDescriptionShort, Strings.Storj.StorjSharedAccessDescriptionLong),
                    new CommandLineArgument(STORJ_BUCKET, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjBucketDescriptionShort, Strings.Storj.StorjBucketDescriptionLong),
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

        public string[] DNSName
        {
            get
            {
                return new string[0];
            }
        }

        public void CreateFolder()
        {
            //Storj DCS has no folders
        }

        public void Delete(string remotename)
        {
            var deleteTask = DeleteAsync(remotename);
            deleteTask.Wait();
        }

        public async Task DeleteAsync(string remotename)
        {
            try
            {
                var bucket = await _bucketService.EnsureBucketAsync(_bucket);
                await _objectService.DeleteObjectAsync(bucket, GetBasePath() + remotename);
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

        public void Get(string remotename, string filename)
        {
            var getTask = GetAsync(remotename, filename);
            getTask.Wait();
        }

        public async Task GetAsync(string remotename, string filename)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            var download = await _objectService.DownloadObjectAsync(bucket, GetBasePath() + remotename, new DownloadOptions(), false);
            await download.StartDownloadAsync();

            if (download.Completed)
            {
                using (FileStream file = new FileStream(filename, FileMode.Create))
                {
                    await file.WriteAsync(download.DownloadedBytes, 0, (int)download.BytesReceived);
                    await file.FlushAsync().ConfigureAwait(false);
                }
            }
        }

        public void Get(string remotename, Stream stream)
        {
            var getTask = GetAsync(remotename, stream);
            getTask.Wait();
        }

        public async Task GetAsync(string remotename, Stream stream)
        {
            int index = 0;
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            var download = await _objectService.DownloadObjectAsync(bucket, GetBasePath() + remotename, new DownloadOptions(), false);
            download.DownloadOperationProgressChanged += (op) =>
            {
                int newPartLength = (int)op.BytesReceived - index;
                byte[] newPart = new byte[newPartLength];
                Array.Copy(op.DownloadedBytes, index, newPart, 0, newPartLength);
                stream.Write(newPart, 0, newPartLength);
                index = index + newPartLength;
            };
            await download.StartDownloadAsync();
        }

        public IEnumerable<IFileEntry> List()
        {
            var listTask = ListAsync();
            listTask.Wait();
            return listTask.Result;
        }

        private async Task<IEnumerable<IFileEntry>> ListAsync()
        {
            List<StorjFile> files = new List<StorjFile>();
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            var prefix = GetBasePath();
            var objects = await _objectService.ListObjectsAsync(bucket, new ListObjectsOptions { Recursive = true, System = true, Custom = true, Prefix = prefix });

            foreach (var obj in objects.Items)
            {
                StorjFile file = new StorjFile(obj);
                if (prefix != "")
                {
                    file.Name = file.Name.Replace(prefix, "");
                }
                files.Add(file);
            }

            return files;
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            CustomMetadata custom = new CustomMetadata();
            custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_ACCESS, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            custom.Entries.Add(new CustomMetadataEntry { Key = StorjFile.STORJ_LAST_MODIFICATION, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            var upload = await _objectService.UploadObjectAsync(bucket, GetBasePath() + remotename, new UploadOptions(), stream, custom, false);
            await upload.StartUploadAsync();
        }

        public void Test()
        {
            var testTask = TestAsync();
            testTask.Wait(10000);
            if (!testTask.Result)
            {
                throw new Exception(Strings.Storj.TestConnectionFailed);
            }
        }

        /// <summary>
        /// Test the connection by:
        /// - creating the bucket (if it not already exists)
        /// - uploading 256 random bytes to a test-file
        /// - downloading the file back and expecting 256 bytes
        /// </summary>
        /// <returns>true, if the test was successfull or and exception</returns>
        private async Task<bool> TestAsync()
        {
            string testFileName = GetBasePath() + "duplicati_test.dat";

            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            var upload = await _objectService.UploadObjectAsync(bucket, testFileName, new UploadOptions(), GetRandomBytes(256), false);
            await upload.StartUploadAsync();

            var download = await _objectService.DownloadObjectAsync(bucket, testFileName, new DownloadOptions(), false);
            await download.StartDownloadAsync();

            await _objectService.DeleteObjectAsync(bucket, testFileName);

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
