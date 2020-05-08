using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uplink.NET.Interfaces;
using uplink.NET.Models;
using uplink.NET.Services;

namespace Duplicati.Library.Backend.Tardigrade
{
    public class Tardigrade : IBackend
    {
        private const string TARDIGRADE_AUTH_METHOD = "tardigrade-auth-method";
        private const string TARDIGRADE_SATELLITE = "tardigrade-satellite";
        private const string TARDIGRADE_API_KEY = "tardigrade-api-key";
        private const string TARDIGRADE_SECRET = "tardigrade-secret";
        private const string TARDIGRADE_SHARED_ACCESS = "tardigrade-shared-access";
        private const string TARDIGRADE_BUCKET = "tardigrade-bucket";
        private const string TARDIGRADE_FOLDER = "tardigrade-folder";

        private readonly string _auth_method;
        private readonly string _satellite;
        private readonly string _api_key;
        private readonly string _secret;
        private readonly string _shared_access;
        private readonly string _bucket;
        private readonly string _folder;
        private Access _access;
        private IBucketService _bucketService;
        private IObjectService _objectService;

        public static readonly Dictionary<string, string> KNOWN_TARDIGRADE_SATELLITES = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "US Central 1", "us-central-1" },
            { "Asia East 1", "asia-east-1" },
            { "Europe West 1", "europe-west-1" },
        };

        public static readonly Dictionary<string, string> KNOWN_AUTHENTICATION_METHODS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "API key", "API key" },
            { "Access grant", "Access grant" },
        };

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            _auth_method = options[TARDIGRADE_AUTH_METHOD];

            Access.SetTempDirectory(System.IO.Path.GetTempPath());

            if (_auth_method == "Access grant")
            {
                //Create an access from the access grant
                _shared_access = options[TARDIGRADE_SHARED_ACCESS];
                _access = new Access(_shared_access);
            }
            else
            {
                //Create an access for a satellite, API key and encryption passphrase
                _satellite = options[TARDIGRADE_SATELLITE];

                //If the satellite is from the list of known satellites, attach the domain and port
                if (KNOWN_TARDIGRADE_SATELLITES.Where(s => s.Value == _satellite).Count() == 1)
                    _satellite = _satellite + ".tardigrade.io:7777";

                if (options.ContainsKey(TARDIGRADE_API_KEY))
                    _api_key = options[TARDIGRADE_API_KEY];
                if (options.ContainsKey(TARDIGRADE_SECRET))
                    _secret = options[TARDIGRADE_SECRET];

                _access = new Access(_satellite, _api_key, _secret);
            }

            _bucketService = new BucketService(_access);
            _objectService = new ObjectService(_access);

            //If no bucket was provided use the default "duplicati"-bucket
            if (options.ContainsKey(TARDIGRADE_BUCKET))
                _bucket = options[TARDIGRADE_BUCKET];
            else
                _bucket = "duplicati";

            if (options.ContainsKey(TARDIGRADE_FOLDER))
                _folder = options[TARDIGRADE_FOLDER];
        }

        public string DisplayName
        {
            get { return Strings.Tardigrade.DisplayName; }
        }

        public string ProtocolKey => "tardigrade";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(TARDIGRADE_AUTH_METHOD, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeAuthMethodDescriptionShort, Strings.Tardigrade.TardigradeAuthMethodDescriptionLong, "API key"),
                    new CommandLineArgument(TARDIGRADE_SATELLITE, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeSatelliteDescriptionShort, Strings.Tardigrade.TardigradeSatelliteDescriptionShort, "us-central-1"),
                    new CommandLineArgument(TARDIGRADE_API_KEY, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeAPIKeyDescriptionShort, Strings.Tardigrade.TardigradeAPIKeyDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_SECRET, CommandLineArgument.ArgumentType.Password, Strings.Tardigrade.TardigradeSecretDescriptionShort, Strings.Tardigrade.TardigradeSecretDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_SHARED_ACCESS, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeSharedAccessDescriptionShort, Strings.Tardigrade.TardigradeSharedAccessDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_BUCKET, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeBucketDescriptionShort, Strings.Tardigrade.TardigradeBucketDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_FOLDER, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeFolderDescriptionShort, Strings.Tardigrade.TardigradeFolderDescriptionLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.Tardigrade.Description;
            }
        }

        public string[] DNSName => throw new NotImplementedException();

        public void CreateFolder()
        {
            //Tardigrade has no folders
        }

        public void Delete(string remotename)
        {
            var deleteTask = DeleteAsync(remotename);
            deleteTask.Wait();
        }

        public async Task DeleteAsync(string remotename)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            await _objectService.DeleteObjectAsync(bucket, remotename);
        }

        public void Dispose()
        {
            if (_objectService != null)
                _objectService = null;
            if (_bucketService != null)
                _bucketService = null;
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

            if(download.Completed)
            {
                using (FileStream file = new FileStream(filename, FileMode.Create))
                {
                    await file.WriteAsync(download.DownloadedBytes, 0, (int)download.BytesReceived);
                    await file.FlushAsync();
                }
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            var listTask = ListAsync();
            listTask.Wait();
            return listTask.Result;
        }

        private async Task<IEnumerable<IFileEntry>> ListAsync()
        {
            List<TardigradeFile> files = new List<TardigradeFile>();
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            var objects = await _objectService.ListObjectsAsync(bucket, new ListObjectsOptions() { Recursive = true, System = true, Custom = true });

            foreach(var obj in objects.Items)
            {
                TardigradeFile file = new TardigradeFile(obj);
                file.Name = file.Name.Replace(GetBasePath(), "");
                files.Add(file);
            }

            return files;
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            var bucket = await _bucketService.EnsureBucketAsync(_bucket);
            CustomMetadata custom = new CustomMetadata();
            custom.Entries.Add(new CustomMetadataEntry() { Key = TardigradeFile.TARDIGRADE_LAST_ACCESS, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            custom.Entries.Add(new CustomMetadataEntry() { Key = TardigradeFile.TARDIGRADE_LAST_MODIFICATION, Value = DateTime.Now.ToUniversalTime().ToString("O") });
            var upload = await _objectService.UploadObjectAsync(bucket, GetBasePath() + remotename, new UploadOptions(), new FileStream(filename, FileMode.Open), false);
            await upload.StartUploadAsync();
        }

        public void Test()
        {
            var testTask = TestAsync();
            testTask.Wait(10000);
            if(!testTask.Result)
                throw new Exception(Strings.Tardigrade.TestConnectionFailed);
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
                throw new Exception(download.ErrorMessage);
            
            return true;
        }

        /// <summary>
        /// Gets the base path - depending on there is a folder set or not
        /// </summary>
        /// <returns>The base path within a bucket where the backup shall be placed</returns>
        private string GetBasePath()
        {
            if (!string.IsNullOrEmpty(_folder))
                return "/" + _folder + "/";
            else
                return "/";
        }

        /// <summary>
        /// Creates some random bytes with the given length - just for testing the connection
        /// </summary>
        /// <param name="length">The length of the bytes to create</param>
        /// <returns>A byte-array with the given length</returns>
        private byte[] GetRandomBytes(long length)
        {
            byte[] bytes = new byte[length];
            Random rand = new Random();
            rand.NextBytes(bytes);

            return bytes;
        }
    }
}
