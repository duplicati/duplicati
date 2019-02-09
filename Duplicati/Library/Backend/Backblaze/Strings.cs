using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class B2 {
        public static string B2applicationkeyDescriptionShort { get { return LC.L(@"The ""B2 Cloud Storage Application Key"" can be obtained after logging into your Backblaze account, this can also be supplied through the ""auth-password"" property"); } }
        public static string B2applicationkeyDescriptionLong { get { return LC.L(@"The ""B2 Cloud Storage Application Key"""); } }
        public static string B2accountidDescriptionLong { get { return LC.L(@"The ""B2 Cloud Storage Account ID"" can be obtained after logging into your Backblaze account, this can also be supplied through the ""auth-username"" property"); } }
        public static string B2accountidDescriptionShort { get { return LC.L(@"The ""B2 Cloud Storage Account ID"""); } }
        public static string DisplayName { get { return LC.L(@"B2 Cloud Storage"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string NoB2KeyError { get { return LC.L(@"No ""B2 Cloud Storage Application Key"" given"); } }
        public static string NoB2UserIDError { get { return LC.L(@"No ""B2 Cloud Storage Account ID"" given"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to the Backblaze B2 Cloud Storage. Allowed formats are: ""b2://bucketname/prefix"""); } }
        public static string B2createbuckettypeDescriptionLong { get { return LC.L(@"By default, a private bucket is created. Use this option to set the bucket type. Refer to the B2 documentation for allowed types "); } }
        public static string B2createbuckettypeDescriptionShort { get { return LC.L(@"The bucket type used when creating a bucket"); } }
		public static string B2pagesizeDescriptionLong { get { return LC.L(@"Use this option to set the page size for listing contents of B2 buckets. A lower number means less data, but can increase the number of Class C transaction on B2. Suggested values are between 100 and 1000"); } }
		public static string B2pagesizeDescriptionShort { get { return LC.L(@"The size of file-listing pages"); } }
        public static string B2downloadurlDescriptionLong { get { return LC.L(@"Change this if you want to use your custom domain to download files, and uploading will not be affected. The default download url is ""https://f00X.backblazeb2.com"""); } }
        public static string B2downloadurlDescriptionShort { get { return LC.L(@"The base URL to use for downloading files"); } }
        public static string InvalidPageSizeError(string argname, string value) { return LC.L(@"The setting ""{0}"" is invalid for ""{1}"", it must be an integer larger than zero", value, argname); }
	}
}
