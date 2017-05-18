using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class S3Backend {
        public static string AMZKeyDescriptionLong { get { return LC.L(@"The AWS ""Secret Access Key"" can be obtained after logging into your AWS account, this can also be supplied through the ""auth-password"" property"); } }
        public static string AMZKeyDescriptionShort { get { return LC.L(@"The AWS ""Secret Access Key"""); } }
        public static string AMZUserIDDescriptionLong { get { return LC.L(@"The AWS ""Access Key ID"" can be obtained after logging into your AWS account, this can also be supplied through the ""auth-username"" property"); } }
        public static string AMZUserIDDescriptionShort { get { return LC.L(@"The AWS ""Access Key ID"""); } }
        public static string DisplayName { get { return LC.L(@"Amazon S3"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string NoAMZKeyError { get { return LC.L(@"No Amazon S3 secret key given"); } }
        public static string NoAMZUserIDError { get { return LC.L(@"No Amazon S3 userID given"); } }
        public static string S3EurobucketDescriptionLong { get { return LC.L(@"This flag is only used when creating new buckets. If the flag is set, the bucket is created on a European server. This flag forces the ""s3-use-new-style"" flag. Amazon charges slightly more for European buckets."); } }
        public static string S3EurobucketDescriptionShort { get { return LC.L(@"Use a European server"); } }
        public static string S3NewStyleDescriptionLong { get { return LC.L(@"Specify this argument to make the S3 backend use subdomains rather than the previous url prefix method. See the Amazon S3 documentation for more details."); } }
        public static string S3NewStyleDescriptionShort { get { return LC.L(@"Use subdomain calling style"); } }
        public static string UnableToDecodeBucketnameError(string url) { return LC.L(@"Unable to determine the bucket name for host: {0}", url); }
        public static string S3UseRRSDescriptionLong { get { return LC.L(@"This flag toggles the use of the special RRS header. Files stored using RRS are more likely to disappear than those stored normally, but also costs less to store. See the full description here: http://aws.amazon.com/about-aws/whats-new/2010/05/19/announcing-amazon-s3-reduced-redundancy-storage/"); } }
        public static string S3UseRRSDescriptionShort { get { return LC.L(@"Use Reduced Redundancy Storage"); } }
        public static string DeprecatedUrlFormat(string url) { return LC.L(@"You are using a deprected url format, please change it to: {0}", url); }
        public static string Description_v2 { get { return LC.L(@"This backend can read and write data to an Amazon S3 compatible server. Allowed formats are: ""s3://bucketname/prefix"""); } }
        public static string OptionsAreMutuallyExclusiveError(string option1, string option2) { return LC.L(@"The options --{0} and --{1} are mutually exclusive", option1, option2); }
        public static string S3EurobucketDeprecationDescription(string optionname, string optionvalue) { return LC.L(@"Please use --{0}={1} instead", optionname, optionvalue); }
        public static string S3LocationDescriptionLong(string regions) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what region the data is stored in. Amazon charges slightly more for non-US buckets. Known bucket locations:
{0}", regions); }
        public static string S3LocationDescriptionShort { get { return LC.L(@"Specifies S3 location constraints"); } }
        public static string S3ServerNameDescriptionLong(string providers) { return LC.L(@"Companies other than Amazon are now supporting the S3 API, meaning that this backend can read and write data to those providers as well. Use this option to set the hostname. Currently known providers are:
{0}", providers); }
        public static string S3ServerNameDescriptionShort { get { return LC.L(@"Specifies an alternate S3 server name"); } }
        public static string S3NewStyleDeprecation { get { return LC.L(@"The subdomain calling option does nothing, the library will pick the right calling convention"); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this flag to communicate using Secure Socket Layer (SSL) over http (https). Note that bucket names containing a period has problems with SSL connections."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instructs Duplicati to use an SSL (https) connection"); } }

        public static string S3StorageclassDescriptionLong { get { return LC.L(@"Use this option to specify a storage class. If this option is not used, the server will choose a default storage class."); } }
        public static string S3StorageclassDescriptionShort { get { return LC.L(@"Specify storage class"); } }
        public static string S3RRSDeprecationDescription(string optionname, string optionvalue) { return LC.L(@"Please use --{0}={1} instead", optionname, optionvalue); }
    }
}
