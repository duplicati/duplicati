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
    internal static class WEBDAV {
        public static string Description { get { return LC.L(@"This backend can read and write data to a WEBDAV enabled web server, using the HTTP protocol. Allowed formats are ""webdav://hostname/folder"" and ""webdav://username:password@hostname/folder""."); } }
        public static string DisplayName { get { return LC.L(@"WebDAV"); } }
        public static string DescriptionForceDigestLong { get { return LC.L(@"Using the HTTP Digest authentication method allows the user to authenticate with the server, without sending the password in clear. However, a man-in-the-middle attack is easy, because the HTTP protocol specifies a fallback to Basic authentication, which will make the client send the password to the attacker. Using this option, the client does not accept this, and always uses Digest authentication or fails to connect."); } }
        public static string DescriptionForceDigestShort { get { return LC.L(@"Force the use of the HTTP Digest authentication method"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string DescriptionIntegratedAuthenticationLong { get { return LC.L(@"If the server and client both supports integrated authentication, this option enables that authentication method. This is likely only available with windows servers and clients."); } }
        public static string DescriptionIntegratedAuthenticationShort { get { return LC.L(@"Use windows integrated authentication to connect to the server"); } }
        public static string MethodNotAllowedError(System.Net.HttpStatusCode statuscode) { return LC.L(@"The server returned the error code {0} ({1}), indicating that the server does not support WebDAV connections", (int)statuscode, statuscode); }
		public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found, message: {1}", foldername, message); }
		public static string SeenThenNotFoundError(string foldername, string filename, string extension, string errormessage) { return LC.L(@"When listing the folder {0} the file {1} was listed, but the server now reports that the file is not found.
This can be because the file is deleted or unavailable, but it can also be because the file extension {2} is blocked by the web server. IIS blocks unknown extensions by default.
Error message: {3}", foldername, filename, extension, errormessage); }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this option to communicate using Secure Socket Layer (SSL) over http (https)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instruct Duplicati to use an SSL (https) connection"); } }
        public static string DescriptionDebugPropfindLong { get { return LC.L(@"To aid in debugging issues, it is possible to set a path to a file that will be overwritten with the PROPFIND response."); } }
        public static string DescriptionDebugPropfindShort { get { return LC.L(@"Dump the PROPFIND response"); } }
        public static string DescriptionAcceptAnyCertificateLong { get { return LC.L(@"Use this option to accept any server certificate, regardless of what errors it may have. Please use --{0} instead, whenever possible.", "accept-specified-ssl-hash"); } }
        public static string DescriptionAcceptAnyCertificateShort { get { return LC.L(@"Accept any server certificate"); } }
        public static string DescriptionAcceptHashLong2 { get { return LC.L(@"If your server certificate is reported as invalid (e.g. with self-signed certificates), you can supply the certificate hash (SHA1) to approve it anyway. The hash value must be entered in hex format without spaces or colons. You can enter multiple hashes separated by commas."); } }
        public static string DescriptionAcceptHashShort { get { return LC.L(@"Optionally accept a known SSL certificate"); } }
    }
}
