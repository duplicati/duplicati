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
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class S3 : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static string LOGTAG = Logging.Log.LogTagFromType<S3>();

        public const string RRS_OPTION = "s3-use-rrs";
        public const string STORAGECLASS_OPTION = "s3-storage-class";
        public const string EU_BUCKETS_OPTION = "s3-european-buckets";
        public const string SERVER_NAME = "s3-server-name";
        public const string LOCATION_OPTION = "s3-location-constraint";
        public const string SSL_OPTION = "use-ssl";

        public static readonly KeyValuePair<string, string>[] KNOWN_S3_PROVIDERS = new KeyValuePair<string, string>[] {
            new KeyValuePair<string, string>("Amazon S3", "s3.amazonaws.com"),
            new KeyValuePair<string, string>("Hosteurope", "cs.hosteurope.de"),
            new KeyValuePair<string, string>("Dunkel", "dcs.dunkel.de"),
            new KeyValuePair<string, string>("DreamHost", "objects.dreamhost.com"),
            new KeyValuePair<string, string>("dinCloud - Chicago", "d3-ord.dincloud.com"),
            new KeyValuePair<string, string>("dinCloud - Los Angeles", "d3-lax.dincloud.com"),
            new KeyValuePair<string, string>("IBM COS (S3) Public US", "s3-api.us-geo.objectstorage.softlayer.net"),
            new KeyValuePair<string, string>("Wasabi Hot Storage", "s3.wasabisys.com"),
            new KeyValuePair<string, string>("Wasabi Hot Storage (US West)", "s3.us-west-1.wasabisys.com"),
        };

        //Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
        public static readonly KeyValuePair<string, string>[] KNOWN_S3_LOCATIONS = new KeyValuePair<string, string>[] {
            new KeyValuePair<string, string>("(default)", ""),
            new KeyValuePair<string, string>("Europe (EU)", "EU"),
            new KeyValuePair<string, string>("Europe (EU, Frankfurt)", "eu-central-1"),
            new KeyValuePair<string, string>("Europe (EU, Ireland)", "eu-west-1"),
            new KeyValuePair<string, string>("Europe (EU, London)", "eu-west-2"),
            new KeyValuePair<string, string>("US East (Northern Virginia)", "us-east-1"),
            new KeyValuePair<string, string>("US East (Ohio)", "us-east-2"),
            new KeyValuePair<string, string>("US West (Northern California)", "us-west-1"),
            new KeyValuePair<string, string>("US West (Oregon)", "us-west-2"),
            new KeyValuePair<string, string>("Canada (Central)", "ca-central-1"),
            new KeyValuePair<string, string>("Asia Pacific (Mumbai)", "ap-south-1"),
            new KeyValuePair<string, string>("Asia Pacific (Singapore)", "ap-southeast-1"),
            new KeyValuePair<string, string>("Asia Pacific (Sydney)", "ap-southeast-2"),
            new KeyValuePair<string, string>("Asia Pacific (Tokyo)", "ap-northeast-1"),
            new KeyValuePair<string, string>("Asia Pacific (Seoul)", "ap-northeast-2"),
            new KeyValuePair<string, string>("South America (São Paulo)", "sa-east-1"),
        };

        public static readonly KeyValuePair<string, string>[] DEFAULT_S3_LOCATION_BASED_HOSTS = new KeyValuePair<string, string>[] {
            new KeyValuePair<string, string>("EU", "s3-eu-west-1.amazonaws.com"),
            new KeyValuePair<string, string>("ca-central-1", "s3-ca-central-1.amazonaws.com"),
            new KeyValuePair<string, string>("eu-west-1", "s3-eu-west-1.amazonaws.com"),
            new KeyValuePair<string, string>("eu-west-2", "s3-eu-west-2.amazonaws.com"),
            new KeyValuePair<string, string>("eu-central-1", "s3-eu-central-1.amazonaws.com"),
            new KeyValuePair<string, string>("us-east-1", "s3.amazonaws.com"),
            new KeyValuePair<string, string>("us-east-2", "s3.us-east-2.amazonaws.com"),
            new KeyValuePair<string, string>("us-west-1", "s3-us-west-1.amazonaws.com"),
            new KeyValuePair<string, string>("us-west-2", "s3-us-west-2.amazonaws.com"),
            new KeyValuePair<string, string>("ap-south-1", "s3-ap-south-1.amazonaws.com"),
            new KeyValuePair<string, string>("ap-southeast-1", "s3-ap-southeast-1.amazonaws.com"),
            new KeyValuePair<string, string>("ap-southeast-2", "s3-ap-southeast-2.amazonaws.com"),
            new KeyValuePair<string, string>("ap-northeast-1", "s3-ap-northeast-1.amazonaws.com"),
            new KeyValuePair<string, string>("ap-northeast-2", "s3-ap-northeast-2.amazonaws.com"),
            new KeyValuePair<string, string>("sa-east-1", "s3-sa-east-1.amazonaws.com"),
        };

        public static readonly KeyValuePair<string, string>[] KNOWN_S3_STORAGE_CLASSES;

        static S3() {
            var ns = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("(default)", ""),
                new KeyValuePair<string, string>("Standard", "STANDARD"),
                new KeyValuePair<string, string>("Infrequent Access (IA)", "STANDARD_IA"),
                new KeyValuePair<string, string>("Glacier", "GLACIER"),
                new KeyValuePair<string, string>("Reduced Redundancy Storage (RRS)", "REDUCED_REDUNDANCY"),
            };

            try
            {
                foreach(var sc in ReadStorageClasses())
                    if (!ns.Select(x => x.Value).Contains(sc.Value))
                        ns.Add(sc);
            }
            catch
            {
            }

            KNOWN_S3_STORAGE_CLASSES = ns.ToArray();
        }

        /// <summary>
        /// Fetch storage classes from the API through reflection so we are always updated
        /// </summary>
        /// <returns>The storage classes.</returns>
        private static IEnumerable<KeyValuePair<string, string>> ReadStorageClasses()
        {
            foreach(var f in typeof(Amazon.S3.S3StorageClass).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public))
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

        public const string DEFAULT_S3_HOST  = "s3.amazonaws.com";
        public const string S3_EU_REGION_NAME = "eu-west-1";
        public const string S3_RRS_CLASS_NAME = "REDUCED_REDUNDANCY";

        private Dictionary<string, string> m_options;

        private S3Wrapper m_wrapper;


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
                    foreach(KeyValuePair<string, string> kvp in DEFAULT_S3_LOCATION_BASED_HOSTS)
                        if (kvp.Key.Equals(locationConstraint, StringComparison.OrdinalIgnoreCase))
                        {
                            s3host = kvp.Value;
                            break;
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
                    m_bucket = Library.Utility.Uri.UrlDecode(u.PathAndQuery);

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
                        m_prefix = Library.Utility.Uri.UrlDecode(u.PathAndQuery);

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

            m_options = options;
            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0)
            {
                m_prefix = Duplicati.Library.Utility.Utility.AppendDirSeparator(m_prefix, "/");
            }

            // Auto-disable dns lookup for non AWS configurations
            var hasForcePathStyle = options.ContainsKey("s3-ext-forcepathstyle");
            if (!hasForcePathStyle && !DEFAULT_S3_LOCATION_BASED_HOSTS.Any(x => string.Equals(x.Value, host, StringComparison.OrdinalIgnoreCase)) && !string.Equals(host, "s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
                options["s3-ext-forcepathstyle"] = "true";

            m_wrapper = new S3Wrapper(awsID, awsKey, locationConstraint, host, storageClass, useSSL, options);
        }

        public static bool IsValidHostname(string bucketname)
        {
            if (string.IsNullOrEmpty(bucketname))
                return false;
            else
                return Amazon.S3.Util.AmazonS3Util.ValidateV2Bucket(bucketname);
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
            try
            {
                return ListWithouExceptionCatch();
            }
            catch (Exception ex)
            {
                //Catch "non-existing" buckets
                Amazon.S3.AmazonS3Exception s3ex = ex as Amazon.S3.AmazonS3Exception;
                if (s3ex != null && (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound || "NoSuchBucket".Equals(s3ex.ErrorCode)))
                    throw new Interface.FolderMissingException(ex);

                throw;
            }
        }

        private IEnumerable<IFileEntry> ListWithouExceptionCatch()
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

        public void Put(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        public void Put(string remotename, System.IO.Stream input)
        {
            try
            {
                Connection.AddFileStream(m_bucket, GetFullKey(remotename), input);
            }
            catch (Exception ex)
            {
                //Catch "non-existing" buckets
                Amazon.S3.AmazonS3Exception s3ex = ex as Amazon.S3.AmazonS3Exception;
                if (s3ex != null && (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound || "NoSuchBucket".Equals(s3ex.ErrorCode)))
                    throw new Interface.FolderMissingException(ex);

                throw;
            }
        }

        public void Get(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
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
                foreach(KeyValuePair<string, string> s in KNOWN_S3_PROVIDERS)
                    hostnames.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                foreach (KeyValuePair<string, string> s in KNOWN_S3_LOCATIONS)
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
            if (m_options != null)
                m_options = null;
            if (m_wrapper != null)
            {
                m_wrapper.Dispose();
                m_wrapper = null;
            }
        }

        #endregion

        private S3Wrapper Connection
        {
            get { return m_wrapper; }
        }

        public string[] DNSName
        {
            get { return new string[] { m_wrapper.DNSHost }; }
        }

        private string GetFullKey(string name)
        {
            //AWS SDK encodes the filenames correctly
            return m_prefix + name;
        }
    }
}
