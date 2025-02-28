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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class S3 : IBackend, IStreamingBackend, IRenameEnabledBackend, IFolderEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<S3>();

        private const string STORAGECLASS_OPTION = "s3-storage-class";
        private const string SERVER_NAME = "s3-server-name";
        private const string LOCATION_OPTION = "s3-location-constraint";
        private const string SSL_OPTION = "use-ssl";
        private const string S3_CLIENT_OPTION = "s3-client";
        private const string S3_DISABLE_CHUNK_ENCODING_OPTION = "s3-disable-chunk-encoding";
        private const string S3_LIST_API_VERSION_OPTION = "s3-list-api-version";
        private const string S3_RECURSIVE_LIST = "s3-recursive-list";

        public static readonly Dictionary<string, string> KNOWN_S3_PROVIDERS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Amazon S3", "s3.amazonaws.com" },
            { "MyCloudyPlace (EU)", "s3.mycloudyplace.com" },
            { "Impossible Cloud (US)", "us-west-1.storage.impossibleapi.net" },
            { "Scaleway (Amsterdam, The Netherlands)", "s3.nl-ams.scw.cloud" },
            { "Scaleway (Paris, France)", "s3.fr-par.scw.cloud" },
            { "Scaleway (Warsaw, Poland)", "s3.pl-waw.scw.cloud" },
            { "Hosteurope", "cs.hosteurope.de" },
            { "Dunkel", "dcs.dunkel.de" },
            { "DreamHost", "objects.dreamhost.com" },
            { "dinCloud - Chicago", "d3-ord.dincloud.com" },
            { "dinCloud - Los Angeles", "d3-lax.dincloud.com" },
            { "Poli Systems - 02 (CH)", "s3-02.polisystems.ch" },
            { "Poli Systems - 03 (CH)", "s3-03.polisystems.ch" },
            { "IBM COS (S3) Public US", "s3-api.us-geo.objectstorage.softlayer.net" },
            { "Storadera", "eu-east-1.s3.storadera.com" },
            { "Wasabi Hot Storage", "s3.wasabisys.com" },
            { "Wasabi Hot Storage (US West)", "s3.us-west-1.wasabisys.com" },
            { "Wasabi Hot Storage (EU Central)", "s3.eu-central-1.wasabisys.com" },
            { "Infomaniak Swiss Backup cluster 1", "s3.swiss-backup.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 2", "s3.swiss-backup02.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 3", "s3.swiss-backup03.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 4", "s3.swiss-backup04.infomaniak.com" },
            { "Infomaniak Public Cloud 1", "s3.pub1.infomaniak.cloud" },
            { "Infomaniak Public Cloud 2", "s3.pub2.infomaniak.cloud" },
            { "さくらのクラウド (Sakura Cloud)", "s3.isk01.sakurastorage.jp" },
            { "Seagate Lyve - US-East-1", "https://s3.us-east-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - US-West-1", "https://s3.us-west-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - AP-Southeast-1", "https://s3.ap-southeast-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - EU-West-1", "https://s3.eu-west-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - US-Central-2", "https://s3.us-central-2.lyvecloud.seagate.com" }
        };

        //Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
        public static readonly Dictionary<string, string> KNOWN_S3_LOCATIONS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "(default)", "" },
            { "US East (Ohio)", "us-east-2" },
            { "US East (N. Virginia)", "us-east-1" },
            { "US West (N. California)", "us-west-1" },
            { "US West (Oregon)", "us-west-2" },
            { "Africa (Cape Town)", "af-south-1" },
            { "Asia Pacific (Hong Kong)", "ap-east-1" },
            { "Asia Pacific (Hyderabad)", "ap-south-2" },
            { "Asia Pacific (Jakarta)", "ap-southeast-3" },
            { "Asia Pacific (Melbourne)", "ap-southeast-4" },
            { "Asia Pacific (Mumbai)", "ap-south-1" },
            { "Asia Pacific (Osaka)", "ap-northeast-3" },
            { "Asia Pacific (Seoul)", "ap-northeast-2" },
            { "Asia Pacific (Singapore)", "ap-southeast-1" },
            { "Asia Pacific (Sydney)", "ap-southeast-2" },
            { "Asia Pacific (Tokyo)", "ap-northeast-1" },
            { "Canada (Central)", "ca-central-1" },
            { "Canada West (Calgary)", "ca-west-1" },
            { "Europe (Frankfurt)", "eu-central-1" },
            { "Europe (Ireland)", "eu-west-1" },
            { "Europe (London)", "eu-west-2" },
            { "Europe (Milan)", "eu-south-1" },
            { "Europe (Paris)", "eu-west-3" },
            { "Europe (Spain)", "eu-south-2" },
            { "Europe (Stockholm)", "eu-north-1" },
            { "Europe (Zurich)", "eu-central-2" },
            { "Israel (Tel Aviv)", "il-central-1" },
            { "Middle East (Bahrain)", "me-south-1" },
            { "Middle East (UAE)", "me-central-1" },
            { "South America (São Paulo)", "sa-east-1" },
            { "AWS GovCloud (US-East)", "us-gov-east-1" },
            { "AWS GovCloud (US-West)", "us-gov-west-1" },

            // No longer listed on the AWS site
            { "China (Beijing)", "cn-north-1" },
            { "China (Ningxia)", "cn-northwest-1" },

            // For backwards compatibility, should no longer be used
            { "EU", "eu-west-1" }
        };

        public static readonly Dictionary<string, string> DEFAULT_S3_LOCATION_BASED_HOSTS;

        public static readonly Dictionary<string, string> KNOWN_S3_STORAGE_CLASSES;

        static S3()
        {
            var ns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "(default)", "" },
                { "Standard", "STANDARD" },
                { "Infrequent Access (IA)", "STANDARD_IA" },
                { "One Zone Infrequent Access (One Zone IA)", "ONEZONE_IA" },
                { "Glacier", "GLACIER" },
                { "Deep Archive", "DEEP_ARCHIVE" },
                { "Reduced Redundancy Storage (RRS)", "REDUCED_REDUNDANCY" },
            };

            try
            {
                foreach (var sc in ReadStorageClasses())
                    if (!ns.Select(x => x.Value).Contains(sc.Value, StringComparer.OrdinalIgnoreCase))
                        ns.Add(sc.Key, sc.Value);
            }
            catch
            {
            }

            KNOWN_S3_STORAGE_CLASSES = ns;

            DEFAULT_S3_LOCATION_BASED_HOSTS = KNOWN_S3_LOCATIONS
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => new KeyValuePair<string, string>(x.Value, $"s3.{x.Value}.amazonaws.com"))
                .Append(new KeyValuePair<string, string>("EU", "s3.eu-west-1.amazonaws.com"))
                .DistinctBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
        /// <summary>
        /// Fetch storage classes from the API through reflection so we are always updated
        /// </summary>
        /// <returns>The storage classes.</returns>
        private static IEnumerable<KeyValuePair<string, string>> ReadStorageClasses()
        {
            foreach (var f in typeof(Amazon.S3.S3StorageClass).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public))
            {
                if (f.FieldType == typeof(Amazon.S3.S3StorageClass))
                {
                    var name = new Regex("([a-z])([A-Z])").Replace(f.Name, "$1 $2");
                    var prop = f.GetValue(null) as Amazon.S3.S3StorageClass;
                    if (prop != null && prop.Value != null)
                        yield return new KeyValuePair<string, string>(name, prop.Value);
                }
            }
        }

        private readonly string m_bucket;
        private readonly string m_prefix;
        private readonly bool m_recurseLists;

        private const string DEFAULT_S3_HOST = "s3.amazonaws.com";
        private IS3Client s3Client;

        public S3()
        {
        }

        public S3(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            m_bucket = uri.Host;
            m_prefix = uri.Path;

            if (!options.TryGetValue("aws-access-key-id", out var awsID))
                options.TryGetValue("auth-username", out awsID);
            if (!options.TryGetValue("aws-secret-access-key", out var awsKey))
                options.TryGetValue("auth-password", out awsKey);

            if (!string.IsNullOrEmpty(uri.Username))
                awsID = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                awsKey = uri.Password;

            if (string.IsNullOrEmpty(awsID))
                throw new UserInformationException(Strings.S3Backend.NoAMZUserIDError, "S3NoAmzUserID");
            if (string.IsNullOrEmpty(awsKey))
                throw new UserInformationException(Strings.S3Backend.NoAMZKeyError, "S3NoAmzKey");

            var useSSL = Utility.Utility.ParseBoolOption(options, SSL_OPTION);
            options.TryGetValue(LOCATION_OPTION, out var locationConstraint);
            options.TryGetValue(STORAGECLASS_OPTION, out var storageClass);

            options.TryGetValue(SERVER_NAME, out var hostname);
            if (string.IsNullOrEmpty(hostname))
            {
                hostname = DEFAULT_S3_HOST;

                //Change in S3, now requires that you use location specific endpoint
                if (!string.IsNullOrEmpty(locationConstraint))
                {
                    if (DEFAULT_S3_LOCATION_BASED_HOSTS.TryGetValue(locationConstraint, out var s3hostmatch))
                        hostname = s3hostmatch;
                }
            }

            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0)
                m_prefix = Util.AppendDirSeparator(m_prefix, "/");

            m_recurseLists = Utility.Utility.ParseBoolOption(options, S3_RECURSIVE_LIST);

            // Auto-disable DNS lookup for non-AWS configurations
            if (!options.ContainsKey("s3-ext-forcepathstyle") && !hostname.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
                options["s3-ext-forcepathstyle"] = "true";

            var disableChunkEncoding = Utility.Utility.ParseBoolOption(options, S3_DISABLE_CHUNK_ENCODING_OPTION);

            var s3ClientOptionValue = options.GetValueOrDefault(S3_CLIENT_OPTION);

            if (string.IsNullOrWhiteSpace(s3ClientOptionValue) || string.Equals(s3ClientOptionValue, "aws", StringComparison.OrdinalIgnoreCase))
            {
                s3Client = new S3AwsClient(awsID, awsKey, locationConstraint, hostname, storageClass, useSSL, disableChunkEncoding, options);
            }
            else if (string.Equals(s3ClientOptionValue, "minio", StringComparison.OrdinalIgnoreCase))
            {
                s3Client = new S3MinioClient(awsID, awsKey, locationConstraint, hostname, storageClass, useSSL, options);
            }
            else
            {
                throw new UserInformationException(Strings.S3Backend.UnknownS3ClientError(s3ClientOptionValue), "UnknownS3Client");
            }
        }

        public static bool IsValidHostname(string bucketname)
        {
            return !string.IsNullOrEmpty(bucketname) && Amazon.S3.Util.AmazonS3Util.ValidateV2Bucket(bucketname);
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.S3Backend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "s3"; }
        }

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken)
            => Connection.ListBucketAsync(m_bucket, m_prefix, m_recurseLists, cancelToken);

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            await Connection.AddFileStreamAsync(m_bucket, GetFullKey(remotename), input, cancelToken);
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (var fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task GetAsync(string remotename, Stream output, CancellationToken cancelToken)
        {
            return Connection.GetFileStreamAsync(m_bucket, GetFullKey(remotename), output, cancelToken);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            return Connection.DeleteObjectAsync(m_bucket, GetFullKey(remotename), cancelToken);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                StringBuilder hostnames = new StringBuilder();
                StringBuilder locations = new StringBuilder();
                foreach (var s in KNOWN_S3_PROVIDERS)
                    hostnames.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                foreach (var s in KNOWN_S3_LOCATIONS)
                    locations.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                var exts = S3AwsClient.GetAwsExtendedOptions();

                var normal = new ICommandLineArgument[] {
                    new CommandLineArgument("aws-secret-access-key", CommandLineArgument.ArgumentType.Password, Strings.S3Backend.AMZKeyDescriptionShort, Strings.S3Backend.AMZKeyDescriptionLong,null, new string[] {"auth-password"}, null ),
                    new CommandLineArgument("aws-access-key-id", CommandLineArgument.ArgumentType.String, Strings.S3Backend.AMZUserIDDescriptionShort, Strings.S3Backend.AMZUserIDDescriptionLong, null, new string[] {"auth-username"}, null),
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3StorageclassDescriptionShort, Strings.S3Backend.S3StorageclassDescriptionLong),
                    new CommandLineArgument(SERVER_NAME, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3ServerNameDescriptionShort, Strings.S3Backend.S3ServerNameDescriptionLong(hostnames.ToString()), DEFAULT_S3_HOST),
                    new CommandLineArgument(LOCATION_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3LocationDescriptionShort, Strings.S3Backend.S3LocationDescriptionLong(locations.ToString())),
                    new CommandLineArgument(SSL_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionUseSSLShort, Strings.S3Backend.DescriptionUseSSLLong),
                    new CommandLineArgument(S3_CLIENT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.S3Backend.S3ClientDescriptionShort, Strings.S3Backend.S3ClientDescriptionLong, "aws", null, new string[] { "aws", "minio" }),
                    new CommandLineArgument(S3_DISABLE_CHUNK_ENCODING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionDisableChunkEncodingShort, Strings.S3Backend.DescriptionDisableChunkEncodingLong, "false"),
                    new CommandLineArgument(S3_LIST_API_VERSION_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.S3Backend.DescriptionListApiVersionShort, Strings.S3Backend.DescriptionListApiVersionLong, "v1", null, ["v1", "v2"]),
                    new CommandLineArgument(S3_RECURSIVE_LIST, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionRecursiveListShort, Strings.S3Backend.DescriptionRecursiveListLong, "false"),
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.S3Backend.AuthPasswordDescriptionShort, Strings.S3Backend.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.S3Backend.AuthUsernameDescriptionShort, Strings.S3Backend.AuthUsernameDescriptionLong),
                };

                return normal.Union(exts).ToList();

            }
        }

        public string Description
        {
            get
            {
                return Strings.S3Backend.Description_v2;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            //S3 does not complain if the bucket already exists
            return Connection.AddBucketAsync(m_bucket, cancelToken);
        }

        #endregion

        #region IRenameEnabledBackend Members

        public Task RenameAsync(string source, string target, CancellationToken cancelToken)
        {
            return Connection.RenameFileAsync(m_bucket, GetFullKey(source), GetFullKey(target), cancelToken);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            s3Client?.Dispose();
            s3Client = null;
        }

        #endregion

        private IS3Client Connection
        {
            get { return s3Client; }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { s3Client.GetDnsHost() });

        private string GetFullKey(string name)
        {
            //AWS SDK encodes the filenames correctly
            return m_prefix + name;
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<IFileEntry> ListAsync(string path, CancellationToken cancellationToken)
        {
            var filterPath = GetFullKey(path);
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            return s3Client.ListBucketAsync(m_bucket, filterPath, m_recurseLists, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<IFileEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry>(null);
    }
}
