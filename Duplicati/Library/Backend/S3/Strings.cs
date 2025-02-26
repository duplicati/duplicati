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
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class S3Backend
    {
        public static string Description_v2 { get { return LC.L(@"This backend can read and write data to an S3 compatible server. Allowed format is ""s3://bucketname/prefix""."); } }
        public static string DisplayName { get { return LC.L(@"S3 compatible"); } }
        public static string AMZKeyDescriptionLong { get { return LC.L(@"AWS Secret Access Key can be obtained after logging into your AWS account. This can also be supplied through the option --{0}.", "auth-password"); } }
        public static string AMZKeyDescriptionShort { get { return LC.L(@"AWS Secret Access Key"); } }
        public static string AMZUserIDDescriptionLong { get { return LC.L(@"AWS Access Key ID can be obtained after logging into your AWS account. This can also be supplied through the option --{0}.", "auth-username"); } }
        public static string AMZUserIDDescriptionShort { get { return LC.L(@"AWS Access Key ID"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string NoAMZKeyError { get { return LC.L(@"No S3 secret key given"); } }
        public static string NoAMZUserIDError { get { return LC.L(@"No S3 userID given"); } }
        public static string S3LocationDescriptionLong(string regions) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what region the data is stored in. Amazon charges slightly more for non-US buckets. Known bucket locations:
{0}", regions); }
        public static string S3LocationDescriptionShort { get { return LC.L(@"Specify S3 location constraints"); } }
        public static string S3ServerNameDescriptionLong(string providers) { return LC.L(@"Companies other than Amazon are now supporting the S3 API, meaning that this backend can read and write data to those providers as well. Use this option to set the hostname. Currently known providers are:
{0}", providers); }
        public static string S3ServerNameDescriptionShort { get { return LC.L(@"Specify an alternate S3 server name"); } }
        public static string S3ClientDescriptionLong { get { return LC.L(@"Set either to aws or minio. Then either the AWS SDK or Minio SDK will be used to communicate with S3 services."); } }
        public static string S3ClientDescriptionShort { get { return LC.L(@"Specify the S3 client library to use"); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this option to communicate using Secure Socket Layer (SSL) over http (https). Note that bucket names containing a period has problems with SSL connections."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instruct Duplicati to use an SSL (https) connection"); } }
        public static string DescriptionDisableChunkEncodingLong { get { return LC.L(@"This disables chunk encoding for the aws client, which is not supported by all S3 providers."); } }
        public static string DescriptionDisableChunkEncodingShort { get { return LC.L(@"Disable chunk encoding (aws client only)"); } }
        public static string S3StorageclassDescriptionLong { get { return LC.L(@"Use this option to specify a storage class. If this option is not used, the server will choose a default storage class."); } }
        public static string S3StorageclassDescriptionShort { get { return LC.L(@"Specify storage class"); } }
        public static string DescriptionListApiVersionShort { get { return LC.L(@"Specify the S3 list API version to use"); } }
        public static string DescriptionListApiVersionLong { get { return LC.L(@"Use this option to specify the S3 list API version to use. This can be used to work around issues with some S3 providers."); } }
        public static string DescriptionRecursiveListShort { get { return LC.L(@"Use this option to list all files in the bucket"); } }
        public static string DescriptionRecursiveListLong { get { return LC.L(@"To reduce the number of objects listed, the default is to only list the first level of objects. Use this option to list all objects in the bucket."); } }
        public static string UnknownS3ClientError(string client) { return LC.L(@"Unknown S3 client: {0}", client); }
    }
}
