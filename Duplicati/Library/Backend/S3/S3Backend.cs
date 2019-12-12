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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class S3 : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<S3>();

        private const string RRS_OPTION = "s3-use-rrs";
        private const string STORAGECLASS_OPTION = "s3-storage-class";
        private const string EU_BUCKETS_OPTION = "s3-european-buckets";
        private const string SERVER_NAME = "s3-server-name";
        private const string LOCATION_OPTION = "s3-location-constraint";
        private const string SSL_OPTION = "use-ssl";
        private const string S3_CLIENT_OPTION = "s3-client";

        public static readonly Dictionary<string, string> KNOWN_S3_PROVIDERS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Amazon S3", "s3.amazonaws.com" },
            { "Hosteurope", "cs.hosteurope.de" },
            { "Dunkel", "dcs.dunkel.de" },
            { "DreamHost", "objects.dreamhost.com" },
            { "dinCloud - Chicago", "d3-ord.dincloud.com" },
            { "dinCloud - Los Angeles", "d3-lax.dincloud.com" },
            { "IBM COS (S3) Public US", "s3-api.us-geo.objectstorage.softlayer.net" },
            { "Wasabi Hot Storage", "s3.wasabisys.com" },
            { "Wasabi Hot Storage (US West)", "s3.us-west-1.wasabisys.com" },
            { "Wasabi Hot Storage (EU Central)", "s3.eu-central-1.wasabisys.com" },
        };

        //Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
        public static readonly Dictionary<string, string> KNOWN_S3_LOCATIONS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "(default)", "" },
            { "Europe (EU)", "EU" },
            { "Europe (EU, Frankfurt)", "eu-central-1" },
            { "Europe (EU, Ireland)", "eu-west-1" },
            { "Europe (EU, London)", "eu-west-2" },
            { "Europe (EU, Paris)", "eu-west-3" },
            { "Europe (EU, Stockholm)", "eu-north-1" },
            { "US East (Northern Virginia)", "us-east-1" },
            { "US East (Ohio)", "us-east-2" },
            { "US West (Northern California)", "us-west-1" },
            { "US West (Oregon)", "us-west-2" },
            { "Canada (Central)", "ca-central-1" },
            { "Asia Pacific (Hong Kong)", "ap-east-1" },
            { "Asia Pacific (Mumbai)", "ap-south-1" },
            { "Asia Pacific (Singapore)", "ap-southeast-1" },
            { "Asia Pacific (Sydney)", "ap-southeast-2" },
            { "Asia Pacific (Tokyo)", "ap-northeast-1" },
            { "Asia Pacific (Seoul)", "ap-northeast-2" },
            { "Asia Pacific (Osaka-Local)", "ap-northeast-3" },
            { "South America (São Paulo)", "sa-east-1" },
            { "China (Beijing)", "cn-north-1" },
            { "China (Ningxia)", "cn-northwest-1" },
            { "Middle East (Bahrain)", "me-south-1" },
        };

        public static readonly Dictionary<string, string> DEFAULT_S3_LOCATION_BASED_HOSTS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "EU", "s3.eu-west-1.amazonaws.com" },
            { "ca-central-1", "s3.ca-central-1.amazonaws.com" },
            { "eu-west-1", "s3.eu-west-1.amazonaws.com" },
            { "eu-west-2", "s3.eu-west-2.amazonaws.com" },
            { "eu-west-3", "s3.eu-west-3.amazonaws.com" },
            { "eu-north-1", "s3.eu-north-1.amazonaws.com" },
            { "eu-central-1", "s3.eu-central-1.amazonaws.com" },
            { "us-east-1", "s3.amazonaws.com" },
            { "us-east-2", "s3.us-east-2.amazonaws.com" },
            { "us-west-1", "s3.us-west-1.amazonaws.com" },
            { "us-west-2", "s3.us-west-2.amazonaws.com" },
            { "ap-east-1", "s3.ap-east-1.amazonaws.com" },
            { "ap-south-1", "s3.ap-south-1.amazonaws.com" },
            { "ap-southeast-1", "s3.ap-southeast-1.amazonaws.com" },
            { "ap-southeast-2", "s3.ap-southeast-2.amazonaws.com" },
            { "ap-northeast-1", "s3.ap-northeast-1.amazonaws.com" },
            { "ap-northeast-2", "s3.ap-northeast-2.amazonaws.com" },
            { "ap-northeast-3", "s3.ap-northeast-3.amazonaws.com" },
            { "sa-east-1", "s3.sa-east-1.amazonaws.com" },
            { "cn-north-1", "s3.cn-north-1.amazonaws.com.cn" },
            { "cn-northwest-1", "s3.cn-northwest-1.amazonaws.com.cn" },
            { "me-south-1", "s3.me-south-1.amazonaws.com.cn" },
        };

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

        private const string DEFAULT_S3_HOST = "s3.amazonaws.com";
        private const string S3_EU_REGION_NAME = "eu-west-1";
        private const string S3_RRS_CLASS_NAME = "REDUCED_REDUNDANCY";
        
        private IS3Client s3Client;
        
        public S3()
        {
        }

        public S3(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            string host = uri.Host;
            m_prefix = uri.Path;

            string awsID = null;
            string awsKey = null;

            if (options.ContainsKey("auth-username"))
                awsID = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                awsKey = options["auth-password"];
            
            if (options.ContainsKey("aws_access_key_id"))
                awsID = options["aws_access_key_id"];
            if (options.ContainsKey("aws_secret_access_key"))
                awsKey = options["aws_secret_access_key"];
            if (!string.IsNullOrEmpty(uri.Username))
                awsID = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                awsKey = uri.Password;

            if (string.IsNullOrEmpty(awsID))
                throw new UserInformationException(Strings.S3Backend.NoAMZUserIDError, "S3NoAmzUserID");
            if (string.IsNullOrEmpty(awsKey))
                throw new UserInformationException(Strings.S3Backend.NoAMZKeyError, "S3NoAmzKey");

            bool euBuckets = Utility.Utility.ParseBoolOption(options, EU_BUCKETS_OPTION);
            bool useRRS = Utility.Utility.ParseBoolOption(options, RRS_OPTION);
            bool useSSL = Utility.Utility.ParseBoolOption(options, SSL_OPTION);

            string locationConstraint;
            options.TryGetValue(LOCATION_OPTION, out locationConstraint);

            if (!string.IsNullOrEmpty(locationConstraint) && euBuckets)
                throw new UserInformationException(Strings.S3Backend.OptionsAreMutuallyExclusiveError(LOCATION_OPTION, EU_BUCKETS_OPTION), "S3CannotMixLocationAndEuOptions");

            if (euBuckets)
                locationConstraint = S3_EU_REGION_NAME;

            string storageClass;
            options.TryGetValue(STORAGECLASS_OPTION, out storageClass);
            if (string.IsNullOrWhiteSpace(storageClass) && useRRS)
                storageClass = S3_RRS_CLASS_NAME;

            string s3host;
            options.TryGetValue(SERVER_NAME, out s3host);
            if (string.IsNullOrEmpty(s3host))
            {
                s3host = DEFAULT_S3_HOST;

                //Change in S3, now requires that you use location specific endpoint
                if (!string.IsNullOrEmpty(locationConstraint))
                {
                    if (DEFAULT_S3_LOCATION_BASED_HOSTS.TryGetValue(locationConstraint, out var s3hostmatch))
                        s3host = s3hostmatch;
                }
            }

            //Fallback to previous formats
            if (host.Contains(DEFAULT_S3_HOST))
            {
                Uri u = new Uri(url);
                host = u.Host;
                m_prefix = "";

                if (String.Equals(host, s3host, StringComparison.OrdinalIgnoreCase))
                {
                    m_bucket = Utility.Uri.UrlDecode(u.PathAndQuery);

                    if (m_bucket.StartsWith("/", StringComparison.Ordinal))
                        m_bucket = m_bucket.Substring(1);

                    if (m_bucket.Contains("/"))
                    {
                        m_prefix = m_bucket.Substring(m_bucket.IndexOf("/", StringComparison.Ordinal) + 1);
                        m_bucket = m_bucket.Substring(0, m_bucket.IndexOf("/", StringComparison.Ordinal));
                    }
                }
                else
                {
                    //Subdomain type lookup
                    if (host.EndsWith("." + s3host, StringComparison.OrdinalIgnoreCase))
                    {
                        m_bucket = host.Substring(0, host.Length - ("." + s3host).Length);
                        host = s3host;
                        m_prefix = Utility.Uri.UrlDecode(u.PathAndQuery);

                        if (m_prefix.StartsWith("/", StringComparison.Ordinal))
                            m_prefix = m_prefix.Substring(1);
                    }
                    else
                        throw new UserInformationException(Strings.S3Backend.UnableToDecodeBucketnameError(url), "S3CannotDecodeBucketName");
                }

                Logging.Log.WriteWarningMessage(LOGTAG, "DeprecatedS3Format", null, Strings.S3Backend.DeprecatedUrlFormat("s3://" + m_bucket + "/" + m_prefix));
            }
            else
            {
                //The new simplified url style s3://bucket/prefix
                m_bucket = host;
                host = s3host;
            }

            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0)
            {
                m_prefix = Util.AppendDirSeparator(m_prefix, "/");
            }

            // Auto-disable dns lookup for non AWS configurations
            var hasForcePathStyle = options.ContainsKey("s3-ext-forcepathstyle");
            if (!hasForcePathStyle && !DEFAULT_S3_LOCATION_BASED_HOSTS.Any(x => string.Equals(x.Value, host, StringComparison.OrdinalIgnoreCase)) && !string.Equals(host, "s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
                options["s3-ext-forcepathstyle"] = "true";


            options.TryGetValue(S3_CLIENT_OPTION, out var s3ClientOptionValue);

            if (s3ClientOptionValue == "aws" || s3ClientOptionValue == null)
            {
                s3Client = new S3AwsClient(awsID, awsKey, locationConstraint, host, storageClass, useSSL, options);
            }
            else
            {
                s3Client = new S3MinioClient(awsID, awsKey, locationConstraint, host, storageClass, useSSL, options);
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
        
        
        public IEnumerable<IFileEntry> List()
        {
            foreach (IFileEntry file in Connection.ListBucket(m_bucket, m_prefix))
            {
                ((FileEntry)file).Name = file.Name.Substring(m_prefix.Length);

                //Fix for a bug in Duplicati 1.0 beta 3 and earlier, where filenames are incorrectly prefixed with a slash
                if (file.Name.StartsWith("/", StringComparison.Ordinal) && !m_prefix.StartsWith("/", StringComparison.Ordinal))
                    ((FileEntry)file).Name = file.Name.Substring(1);

                yield return file;
            }
        }

        public Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
                return PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            await Connection.AddFileStreamAsync(m_bucket, GetFullKey(remotename), input, cancelToken);
        }

        public void Get(string remotename, string localname)
        {
            using (var fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        public void Get(string remotename, System.IO.Stream output)
        {
            try
            {
                Connection.GetFileStream(m_bucket, GetFullKey(remotename), output);
            }
            catch
            {
                //This is a fix for the S3 backend prior to beta 3, where the filenames had a slash prefixed
                try
                {
                    if (!remotename.StartsWith("/", StringComparison.Ordinal))
                        Connection.GetFileStream(m_bucket, GetFullKey("/" + remotename), output);
                    return;
                }
                catch
                {
                }

                //Throw original error
                throw;
            }
        }

        public void Delete(string remotename)
        {
            Connection.DeleteObject(m_bucket, GetFullKey(remotename));
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

                var defaults = new Amazon.S3.AmazonS3Config();

                var exts =
                    typeof(Amazon.S3.AmazonS3Config).GetProperties().Where(x => x.CanRead && x.CanWrite && (x.PropertyType == typeof(string) || x.PropertyType == typeof(bool) || x.PropertyType == typeof(int) || x.PropertyType == typeof(long) || x.PropertyType.IsEnum))
                        .Select(x => (ICommandLineArgument)new CommandLineArgument(
                            "s3-ext-" + x.Name.ToLowerInvariant(),
                            x.PropertyType == typeof(bool) ? CommandLineArgument.ArgumentType.Boolean : x.PropertyType.IsEnum ? CommandLineArgument.ArgumentType.Enumeration : CommandLineArgument.ArgumentType.String,
                            x.Name,
                            string.Format("Extended option {0}", x.Name),
                            string.Format("{0}", x.GetValue(defaults)),
                            null,
                            x.PropertyType.IsEnum ? Enum.GetNames(x.PropertyType) : null));


                var normal = new ICommandLineArgument[] {
                    new CommandLineArgument("aws_secret_access_key", CommandLineArgument.ArgumentType.Password, Strings.S3Backend.AMZKeyDescriptionShort, Strings.S3Backend.AMZKeyDescriptionLong, null, new string[] {"auth-password"}, null),
                    new CommandLineArgument("aws_access_key_id", CommandLineArgument.ArgumentType.String, Strings.S3Backend.AMZUserIDDescriptionShort, Strings.S3Backend.AMZUserIDDescriptionLong, null, new string[] {"auth-username"}, null),
                    new CommandLineArgument(EU_BUCKETS_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.S3EurobucketDescriptionShort, Strings.S3Backend.S3EurobucketDescriptionLong, "false", null, null, Strings.S3Backend.S3EurobucketDeprecationDescription(LOCATION_OPTION, S3_EU_REGION_NAME)),
                    new CommandLineArgument(RRS_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.S3UseRRSDescriptionShort, Strings.S3Backend.S3UseRRSDescriptionLong, "false", null, null, Strings.S3Backend.S3RRSDeprecationDescription(STORAGECLASS_OPTION, S3_RRS_CLASS_NAME)),
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3StorageclassDescriptionShort, Strings.S3Backend.S3StorageclassDescriptionLong),
                    new CommandLineArgument(SERVER_NAME, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3ServerNameDescriptionShort, Strings.S3Backend.S3ServerNameDescriptionLong(hostnames.ToString()), DEFAULT_S3_HOST),
                    new CommandLineArgument(LOCATION_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3LocationDescriptionShort, Strings.S3Backend.S3LocationDescriptionLong(locations.ToString())),
                    new CommandLineArgument(SSL_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.DescriptionUseSSLShort, Strings.S3Backend.DescriptionUseSSLLong),
                    new CommandLineArgument(S3_CLIENT_OPTION, CommandLineArgument.ArgumentType.String, Strings.S3Backend.S3ClientDescriptionShort, Strings.S3Backend.DescriptionS3ClientLong),

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

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            //S3 does not complain if the bucket already exists
            Connection.AddBucket(m_bucket);
        }

        #endregion

        #region IRenameEnabledBackend Members

        public void Rename(string source, string target)
        {
            Connection.RenameFile(m_bucket, GetFullKey(source), GetFullKey(target));
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

        public string[] DNSName
        {
            get { return new[] { s3Client.GetDnsHost() }; }
        }

        private string GetFullKey(string name)
        {
            //AWS SDK encodes the filenames correctly
            return m_prefix + name;
        }
    }
}
