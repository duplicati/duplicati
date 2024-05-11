// Copyright (C) 2024, The Duplicati Team
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
using System;
using Duplicati.Library.Localization.Short;
using System.Collections.Generic;

namespace Duplicati.Library.Backend.Strings {
    internal static class FTPBackend {
        public static string Description { get { return LC.L(@"This backend can read and write data to an FTP based backend. Allowed formats are ""ftp://hostname/folder"" or ""ftp://username:password@hostname/folder"""); } }
        public static string DescriptionFTPActiveLong { get { return LC.L(@"If this flag is set, the FTP connection is made in active mode. Even if the ""ftp-passive"" flag is also set, the connection will be made in active mode"); } }
        public static string DescriptionFTPActiveShort { get { return LC.L(@"Toggles the FTP connections method"); } }
        public static string DescriptionFTPPassiveLong { get { return LC.L(@"If this flag is set, the FTP connection is made in passive mode, which works better with some firewalls. If the ""ftp-regular"" flag is also set, this flag is ignored"); } }
        public static string DescriptionFTPPassiveShort { get { return LC.L(@"Toggles the FTP connections method"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this flag to communicate using Secure Socket Layer (SSL) over ftp (ftps)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instructs Duplicati to use an SSL (ftps) connection"); } }
        public static string DisplayName { get { return LC.L(@"FTP"); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found, message: {1}", foldername, message); }
        public static string ListVerifyFailure(string filename, IEnumerable<string> files) { return LC.L(@"The file {0} was uploaded but not found afterwards, the file listing returned {1}", filename, string.Join(Environment.NewLine, files)); }
        public static string ListVerifySizeFailure(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was uploaded but the returned size was {1} and it was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DescriptionDisableUploadVerifyShort { get { return LC.L(@"Disable upload verification"); } }
        public static string DescriptionDisableUploadVerifyLong { get { return LC.L(@"To protect against network failures, every upload will be attempted verified. Use this option to disable this verification to make the upload faster but less reliable."); } }
    }
}
