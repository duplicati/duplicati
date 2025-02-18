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
namespace Duplicati.Library.Backend.Strings {
    internal static class CloudFiles {
        public static string Description_v2 { get { return LC.L(@"This backend can read and write data to CloudFiles. Allowed format is ""cloudfiles://container/folder""."); } }
        public static string DisplayName { get { return LC.L(@"Rackspace CloudFiles"); } }
        public static string DescriptionAuthenticationURLLong_v2(string optionname) { return LC.L(@"CloudFiles use different servers for authentication based on where the account resides. Use this option to set an alternate authentication URL. This option overrides --{0}.", optionname); }
        public static string DescriptionAuthenticationURLShort { get { return LC.L(@"Provide another authentication URL"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string DescriptionPasswordLong { get { return LC.L(@"The API Access Key used to authenticate with CloudFiles."); } }
        public static string DescriptionPasswordShort { get { return LC.L(@"Supply the access key used to connect to the server"); } }
        public static string DescriptionUKAccountLong(string optionname, string optionvalue) { return LC.L(@"Duplicati will assume that the credentials given are for a US account. Use this option if the account is a UK based account. Note that this is equivalent to setting --{0}={1}.", optionname, optionvalue); }
        public static string DescriptionUKAccountShort { get { return LC.L(@"Use a UK account"); } }
        public static string DescriptionUsernameLong { get { return LC.L(@"The username used to authenticate with CloudFiles."); } }
        public static string DescriptionUsernameShort { get { return LC.L(@"Supply the username used to authenticate with CloudFiles"); } }
        public static string ETagVerificationError { get { return LC.L(@"MD5 Hash (ETag) verification failed"); } }
        public static string FileDeleteError { get { return LC.L(@"Failed to delete file"); } }
        public static string FileUploadError { get { return LC.L(@"Failed to upload file"); } }
        public static string NoAPIKeyError { get { return LC.L(@"No CloudFiles API Access Key given"); } }
        public static string NoUserIDError { get { return LC.L(@"No CloudFiles userID given"); } }
        public static string UnexpectedResponseError { get { return LC.L(@"Unexpected CloudFiles response. Perhaps the API has changed?"); } }
    }
}
