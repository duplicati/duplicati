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
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Backend
{
    public class S3 : IBackend, IStreamingBackend, IRenameEnabledBackend, IFolderEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<S3>();

        private const string AUTH_USERNAME_OPTION = "aws-access-key-id";
        private const string AUTH_PASSWORD_OPTION = "aws-secret-access-key";

        private const string STORAGECLASS_OPTION = "s3-storage-class";
        private const string SERVER_NAME = "s3-server-name";
        private const string LOCATION_OPTION = "s3-location-constraint";
        private const string SSL_OPTION = "use-ssl";
        private const string S3_CLIENT_OPTION = "s3-client";
        private const string S3_DISABLE_CHUNK_ENCODING_OPTION = "s3-disable-chunk-encoding";
        private const string S3_DISABLE_PAYLOAD_SIGNING_OPTION = "s3-disable-payload-signing";
        private const string S3_LIST_API_VERSION_OPTION = "s3-list-api-version";
        private const string S3_RECURSIVE_LIST = "s3-recursive-list";

        public static readonly Dictionary<string, string?> KNOWN_S3_PROVIDERS = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            { "Amazon S3", "s3.amazonaws.com" },
            { "Impossible Cloud (US)", "us-west-1.storage.impossibleapi.net" },
            { "Scaleway (Amsterdam, The Netherlands)", "s3.nl-ams.scw.cloud" },
            { "Scaleway (Paris, France)", "s3.fr-par.scw.cloud" },
            { "Scaleway (Warsaw, Poland)", "s3.pl-waw.scw.cloud" },
            { "Hosteurope", "cs.hosteurope.de" },
            { "Dunkel", "dcs.dunkel.de" },
            { "DreamHost", "objects.dreamhost.com" },
            { "Poli Systems - 02 (CH)", "s3-02.polisystems.ch" },
            { "Poli Systems - 03 (CH)", "s3-03.polisystems.ch" },
            { "IBM COS (S3) Public US (legacy SoftLayer)", "s3-api.us-geo.objectstorage.softlayer.net" },
            { "IBM COS (S3) Public US (appdomain)", "s3.us.cloud-object-storage.appdomain.cloud" },
            { "Storadera", "eu-east-1.s3.storadera.com" },
            { "Wasabi US East 1 (N. Virginia)", "s3.wasabisys.com" },
            { "Wasabi US East 2 (N. Virginia)", "s3.us-east-2.wasabisys.com" },
            { "Wasabi US Central 1 (Texas)", "s3.us-central-1.wasabisys.com" },
            { "Wasabi US West 1 (Oregon)", "s3.us-west-1.wasabisys.com" },
            { "Wasabi US West 2 (San Jose)", "s3.us-west-2.wasabisys.com" },
            { "Wasabi CA Central 1 (Toronto)", "s3.ca-central-1.wasabisys.com" },
            { "Wasabi EU Central 1 (Amsterdam)", "s3.eu-central-1.wasabisys.com" },
            { "Wasabi EU Central 2 (Frankfurt)", "s3.eu-central-2.wasabisys.com" },
            { "Wasabi EU West 1 (London)", "s3.eu-west-1.wasabisys.com" },
            { "Wasabi EU West 2 (Paris)", "s3.eu-west-2.wasabisys.com" },
            { "Wasabi EU West 3 (London)", "s3.eu-west-3.wasabisys.com" },
            { "Wasabi EU South 1 (Milan)", "s3.eu-south-1.wasabisys.com" },
            { "Wasabi AP Northeast 1 (Tokyo)", "s3.ap-northeast-1.wasabisys.com" },
            { "Wasabi AP Northeast 2 (Osaka)", "s3.ap-northeast-2.wasabisys.com" },
            { "Wasabi AP Southeast 1 (Singapore)", "s3.ap-southeast-1.wasabisys.com" },
            { "Wasabi AP Southeast 2 (Sydney)", "s3.ap-southeast-2.wasabisys.com" },
            { "Infomaniak Swiss Backup cluster 1", "s3.swiss-backup.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 2", "s3.swiss-backup02.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 3", "s3.swiss-backup03.infomaniak.com" },
            { "Infomaniak Swiss Backup cluster 4", "s3.swiss-backup04.infomaniak.com" },
            { "Infomaniak Public Cloud 1", "s3.pub1.infomaniak.cloud" },
            { "Infomaniak Public Cloud 2", "s3.pub2.infomaniak.cloud" },
            { "さくらのクラウド (Sakura Cloud)", "s3.isk01.sakurastorage.jp" },
            { "Seagate Lyve - US-East-1", "s3.us-east-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - US-West-1", "s3.us-west-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - AP-Southeast-1", "s3.ap-southeast-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - EU-West-1", "s3.eu-west-1.lyvecloud.seagate.com" },
            { "Seagate Lyve - US-Central-2", "s3.us-central-2.lyvecloud.seagate.com" },
            { "Mega S4 - Amsterdam", "eu-central-1.s4.mega.io" },
            { "Mega S4 - Bettembourg", "eu-central-2.s4.mega.io" },
            { "Mega S4 - Montreal", "ca-central-1.s4.mega.io" },
            { "Mega S4 - Vancouver", "ca-west-1.s4.mega.io" },
            { "Rabata US East 1 (Washington)", "s3.us-east-1.rabata.io" },
            { "Rabata EU West 2 (Netherlands)", "s3.eu-west-2.rabata.io" },
            { "Internxt US Central 1 (Texas)", "s3.us-central-1.internxt.com" },
            { "Internxt EU Central 1 (Amsterdam)", "s3.eu-central-1.internxt.com" },
        };

        //Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
        public static readonly Dictionary<string, string?> KNOWN_S3_LOCATIONS = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
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

        public static readonly Dictionary<string, string?> DEFAULT_S3_LOCATION_BASED_HOSTS;

        public static readonly Dictionary<string, string?> KNOWN_S3_STORAGE_CLASSES;

        static S3()
        {
            var ns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
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
                .Select(x => new KeyValuePair<string, string?>(x.Value!, $"s3.{x.Value}.amazonaws.com"))
                .Append(new KeyValuePair<string, string?>("EU", "s3.eu-west-1.amazonaws.com"))
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
        private readonly IS3Client m_s3Client;

        public S3()
        {
            m_bucket = null!;
            m_prefix = null!;
            m_s3Client = null!;
        }

        public S3(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            m_bucket = uri.Host ?? "";
            m_prefix = uri.Path;
            var timeout = TimeoutOptionsHelper.Parse(options);

            var auth = AuthOptionsHelper.ParseWithAlias(options, uri, AUTH_USERNAME_OPTION, AUTH_PASSWORD_OPTION);

            if (!auth.HasUsername)
                throw new UserInformationException(Strings.S3Backend.NoAMZUserIDError, "S3NoAmzUserID");
            if (!auth.HasPassword)
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
                        if (!string.IsNullOrEmpty(s3hostmatch))
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

            // Check if hostname is actually an URL
            if (System.Uri.IsWellFormedUriString(hostname, UriKind.Absolute))
            {
                var hosturi = new System.Uri(hostname);
                if (hosturi.PathAndQuery.Length > 1)
                    throw new UserInformationException(Strings.S3Backend.NoPathAllowedInEndpointError, "S3NoPathInEndpoint");

                hostname = hosturi.Host;
                if (!options.ContainsKey(SSL_OPTION))
                    useSSL = hosturi.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            }

            // Validate that hostname doesn't contain a path
            hostname = hostname.Trim('/').Trim('\\');
            if (hostname.Contains('/') || hostname.Contains('\\'))
                throw new UserInformationException(Strings.S3Backend.NoPathAllowedInEndpointError, "S3NoPathInEndpoint");


            var s3ClientOptionValue = options.GetValueOrDefault(S3_CLIENT_OPTION);

            (var awsID, var awsKey) = auth.GetCredentials();
            if (string.IsNullOrWhiteSpace(s3ClientOptionValue) || string.Equals(s3ClientOptionValue, "aws", StringComparison.OrdinalIgnoreCase))
            {
                var disableChunkEncoding = Utility.Utility.ParseBoolOption(options, S3_DISABLE_CHUNK_ENCODING_OPTION);
                var disablePayloadSigning = Utility.Utility.ParseBoolOption(options, S3_DISABLE_PAYLOAD_SIGNING_OPTION);
                m_s3Client = new S3AwsClient(awsID, awsKey, locationConstraint, hostname, storageClass, useSSL, disableChunkEncoding, disablePayloadSigning, timeout, options);
            }
            else if (string.Equals(s3ClientOptionValue, "minio", StringComparison.OrdinalIgnoreCase))
            {
                m_s3Client = new S3MinioClient(awsID, awsKey, locationConstraint, hostname, storageClass, useSSL, timeout, options);
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

        /// <inheritdoc/>
        public string DisplayName => Strings.S3Backend.DisplayName;

        /// <inheritdoc/>
        public string ProtocolKey => "s3";

        /// <inheritdoc/>
        public bool SupportsStreaming => true;

        /// <inheritdoc/>
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken)
            => Connection.ListBucketAsync(m_bucket, m_prefix, m_recurseLists, cancelToken);

        /// <inheritdoc/>
        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        /// <inheritdoc/>
        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            await Connection.AddFileStreamAsync(m_bucket, GetFullKey(remotename), input, cancelToken);
        }

        /// <inheritdoc/>
        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (var fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task GetAsync(string remotename, Stream output, CancellationToken cancelToken)
            => Connection.GetFileStreamAsync(m_bucket, GetFullKey(remotename), output, cancelToken);

        /// <inheritdoc/>
        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
            => Connection.DeleteObjectAsync(m_bucket, GetFullKey(remotename), cancelToken);

        /// <inheritdoc/>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                var hostnames = new StringBuilder();
                var locations = new StringBuilder();
                foreach (var s in KNOWN_S3_PROVIDERS)
                    hostnames.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                foreach (var s in KNOWN_S3_LOCATIONS)
                    locations.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                var exts = S3AwsClient.GetAwsExtendedOptions();

                return [
                    new CommandLineArgument(AUTH_USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.AMZUserIDDescriptionShort, Strings.S3Backend.AMZUserIDDescriptionLong, null, [AuthOptionsHelper.AuthUsernameOption], null),
                    new CommandLineArgument(AUTH_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.S3Backend.AMZKeyDescriptionShort, Strings.S3Backend.AMZKeyDescriptionLong,null, [AuthOptionsHelper.AuthPasswordOption], null ),
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3StorageclassDescriptionShort, Strings.S3Backend.S3StorageclassDescriptionLong, "", null, KNOWN_S3_STORAGE_CLASSES.Select(x => x.Value).WhereNotNullOrWhiteSpace().ToArray()),
                    new CommandLineArgument(SERVER_NAME, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3ServerNameDescriptionShort, Strings.S3Backend.S3ServerNameDescriptionLong(hostnames.ToString()), DEFAULT_S3_HOST),
                    new CommandLineArgument(LOCATION_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3LocationDescriptionShort, Strings.S3Backend.S3LocationDescriptionLong(locations.ToString())),
                    new CommandLineArgument(SSL_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionUseSSLShort, Strings.S3Backend.DescriptionUseSSLLong),
                    new CommandLineArgument(S3_CLIENT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.S3Backend.S3ClientDescriptionShort, Strings.S3Backend.S3ClientDescriptionLong, "aws", null, new string[] { "aws", "minio" }),
                    new CommandLineArgument(S3_DISABLE_CHUNK_ENCODING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionDisableChunkEncodingShort, Strings.S3Backend.DescriptionDisableChunkEncodingLong, "false"),
                    new CommandLineArgument(S3_DISABLE_PAYLOAD_SIGNING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionDisablePayloadSigningShort, Strings.S3Backend.DescriptionDisablePayloadSigningLong, "false"),
                    new CommandLineArgument(S3AwsClient.S3_ARCHIVE_CLASSES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.S3Backend.S3ArchiveClassesDescriptionShort, Strings.S3Backend.S3ArchiveClassesDescriptionLong, string.Join(",", S3AwsClient.DEFAULT_ARCHIVE_CLASSES.Select(x => x.Value)), null, KNOWN_S3_STORAGE_CLASSES.Select(x => x.Value).WhereNotNullOrWhiteSpace().ToArray()),
                    new CommandLineArgument(S3_LIST_API_VERSION_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.S3Backend.DescriptionListApiVersionShort, Strings.S3Backend.DescriptionListApiVersionLong, "v1", null, ["v1", "v2"]),
                    new CommandLineArgument(S3_RECURSIVE_LIST, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionRecursiveListShort, Strings.S3Backend.DescriptionRecursiveListLong, "false"),
                    .. TimeoutOptionsHelper.GetOptions(),
                    .. exts
                ];
            }
        }

        public string Description => Strings.S3Backend.Description_v2;

        /// <inheritdoc/>
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        /// <inheritdoc/>
        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            //S3 does not complain if the bucket already exists
            return Connection.AddBucketAsync(m_bucket, cancelToken);
        }

        #endregion

        #region IRenameEnabledBackend Members

        /// <inheritdoc/>
        public Task RenameAsync(string source, string target, CancellationToken cancelToken)
            => Connection.RenameFileAsync(m_bucket, GetFullKey(source), GetFullKey(target), cancelToken);

        #endregion

        #region IDisposable Members

        /// <inheritdoc/>
        public void Dispose()
        {
            m_s3Client?.Dispose();
        }

        #endregion

        private IS3Client Connection => m_s3Client;

        /// <inheritdoc/>
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        {
            var host = m_s3Client.GetDnsHost();
            return Task.FromResult<string[]>(
                string.IsNullOrWhiteSpace(host)
                ? []
                : [host]
            );
        }

        private string GetFullKey(string? name)
            //AWS SDK encodes the filenames correctly
            => $"{m_prefix}{name}";

        /// <inheritdoc/>
        public IAsyncEnumerable<IFileEntry> ListAsync(string? path, CancellationToken cancellationToken)
        {
            var filterPath = GetFullKey(path);
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            return m_s3Client.ListBucketAsync(m_bucket, filterPath, m_recurseLists, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(null);
    }
}
