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

using System;
using System.Collections.Generic;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend
{
    internal static class Strings
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to an FTP based backend. Allowed formats are ""ftp://hostname/folder"" and ""ftp://username:password@hostname/folder""."); } }
        public static string DescriptionAlternate { get { return LC.L(@"This backend can read and write data to an FTP based backend. Allowed formats are ""aftp://hostname/folder"" and ""aftp://username:password@hostname/folder""."); } }
        public static string DisplayName { get { return LC.L(@"FTP"); } }
        public static string DisplayNameAlternate { get { return LC.L(@"Alternative FTP"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string DescriptionLogToConsoleLong { get { return LC.L(@"Use this option to log FTP dialog to terminal console for debugging purposes."); } }
        public static string DescriptionLogToConsoleShort { get { return LC.L(@"Log FTP dialog to terminal console"); } }
        public static string DescriptionLogPrivateInfoToConsoleLong { get { return LC.L(@"Use this option to log FTP PRIVATE info (username, password) to console for debugging purposes (DO NOT POST THIS TO THE INTERNET!)"); } }
        public static string DescriptionLogPrivateInfoToConsoleShort { get { return LC.L(@"Log FTP PRIVATE info to console"); } }
        public static string DescriptionLogDiagnosticsShort { get { return LC.L(@"Log diagnostics information"); } }
        public static string DescriptionLogDiagnosticsLong { get { return LC.L(@"Use this option to log diagnostics information to the log output. This can be useful for debugging purposes."); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found. Message: {1}", foldername, message); }
        public static string ListVerifyFailure(string filename, IEnumerable<string> files) { return LC.L(@"The file {0} was uploaded but not found afterwards. The file listing returned {1}", filename, string.Join(Environment.NewLine, files)); }
        public static string ListVerifySizeFailure(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was uploaded but the returned size was {1} and it was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DescriptionDisableUploadVerifyLong { get { return LC.L(@"To protect against network or server failures, every upload will be attempted to be verified. Use this option to disable this verification to make the upload faster but less reliable."); } }
        public static string DescriptionDisableUploadVerifyShort { get { return LC.L(@"Disable upload verification"); } }
        public static string DescriptionFtpDataConnectionTypeLong { get { return LC.L(@"If this flag is set, the FTP data connection type will be changed to the selected option."); } }
        public static string DescriptionFtpDataConnectionTypeShort { get { return LC.L(@"Configure the FTP data connection type"); } }
        public static string DescriptionFtpEncryptionModeLong { get { return LC.L(@"If this flag is set, the FTP encryption mode will be changed to the selected option."); } }
        public static string DescriptionFtpEncryptionModeShort { get { return LC.L(@"Configure the FTP encryption mode"); } }
        public static string DescriptionSslProtocolsLong { get { return LC.L(@"This flag controls the SSL policy to use when encryption is enabled."); } }
        public static string DescriptionSslProtocolsShort { get { return LC.L(@"Configure the SSL policy to use when encryption is enabled"); } }
        public static string DescriptionUploadDelayLong { get { return LC.L(@"Some FTP servers need a small delay before reporting the correct file size. The required delay depends on network topology. If you experience errors related to the upload size not matching, try adding a few seconds delay."); } }
        public static string DescriptionUploadDelayShort { get { return LC.L(@"Add a delay after uploading a file"); } }
        public static string ErrorDeleteFile(string filename, string message) { return LC.L(@"Error on deleting file: {0}, error: {1}", filename, message); }
        public static string ErrorReadFile(string filename, string message) { return LC.L(@"Error reading file: {0}, error: {1}", filename, message); }
        public static string ErrorWriteFile(string filename, string message) { return LC.L(@"Error writing file: {0}, error: {1}", filename, message); }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this option to communicate using Secure Socket Layer (SSL) over ftp (ftps)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instruct Duplicati to use an SSL (ftps) connection"); } }
        public static string DescriptionFTPActiveLong { get { return LC.L(@"Activate this option to make the FTP connection in active mode. Even if the option --{0} is also set, the connection will be made in active mode.", "ftp-passive"); } }
        public static string DescriptionFTPActiveShort { get { return LC.L(@"Toggle the FTP connections method"); } }
        public static string DescriptionFTPPassiveLong { get { return LC.L(@"Activate this option to make the FTP connection in passive mode, which works better with some firewalls. If the option --{0} is set, this option is ignored.", "ftp-regular"); } }
        public static string DescriptionFTPPassiveShort { get { return LC.L(@"Toggle the FTP connections method"); } }
        public static string FtpPassiveDeprecated { get { return LC.L(@"The option ftp-passive is deprecated, use ftp-data-connection-type instead."); } }
        public static string FtpActiveDeprecated { get { return LC.L(@"ftp-regular is deprecated, use ftp-data-connection-type instead."); } }
        public static string UseSslDeprecated { get { return LC.L(@"use-ssl is deprecated, use ftp-ssl-protocols instead."); } }
        public static string FileMissingError(string filename, string message) { return LC.L(@"The file {0} was not found. Message: {1}", filename, message); }
        public static string DescriptionAbsolutePathShort { get { return LC.L(@"Treat the url path as absolute"); } }
        public static string DescriptionAbsolutePathLong { get { return LC.L(@"Use this option to interpret the url path as an absolute path. This option only has an effect if the initial starting folder in the FTP server is not the (virtual) root folder. If not set, the path in the url is treated as relative to the initial login folder."); } }
        public static string DescriptionRelativePathShort { get { return LC.L(@"Treat the url path as relative"); } }
        public static string DescriptionRelativePathLong { get { return LC.L(@"Use this option to interpret the url path as a path that is relative to the initial login folder. This option only has an effect if the initial starting folder in the FTP server is not the (virtual) root folder. If not set, the path in the url is treated as absolute, ignoring the initial login folder."); } }
        public static string DescriptionUseCwdNamesShort { get { return LC.L(@"Use CWD instead of absolute paths"); } }
        public static string DescriptionUseCwdNamesLong { get { return LC.L(@"Use this option to start the connection with a CWD command instead of an absolute path. This can be useful if the FTP server does not support absolute paths."); } }
        public static string ErrorCreateFolder(string targetFolderName, string resultingFolder) { return LC.L(@"Error creating folder {0}, gave folder: {1}", targetFolderName, resultingFolder); }
    }
}
