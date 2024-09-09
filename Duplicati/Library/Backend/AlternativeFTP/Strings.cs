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
using System.Collections.Generic;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.AlternativeFTP
{
    internal static class Strings
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to an FTP based backend using an alternative FTP client. Allowed formats are ""aftp://hostname/folder"" and ""aftp://username:password@hostname/folder""."); } }
        public static string DisplayName { get { return LC.L(@"Alternative FTP"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string DescriptionLogToConsoleLong { get { return LC.L(@"Use this option to log FTP dialog to terminal console for debugging purposes."); } }
        public static string DescriptionLogToConsoleShort { get { return LC.L(@"Log FTP dialog to terminal console"); } }
        public static string DescriptionLogPrivateInfoToConsoleLong { get { return LC.L(@"Use this option to log FTP PRIVATE info (username, password) to console for debugging purposes (DO NOT POST THIS TO THE INTERNET!)"); } }
        public static string DescriptionLogPrivateInfoToConsoleShort { get { return LC.L(@"Log FTP PRIVATE info to console"); } }
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
        public static string ErrorDeleteFile { get { return LC.L(@"Error on deleting file: {0}"); } }
        public static string ErrorReadFile { get { return LC.L(@"Error reading file: {0}"); } }
        public static string ErrorWriteFile { get { return LC.L(@"Error writing file: {0}"); } }
    }
}
