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
    internal static class FileBackend
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to an file based backend. Allowed formats are ""file://hostname/folder"" and ""file://username:password@hostname/folder"". You may supply UNC paths (e.g.: ""file://\\server\folder"") or local paths (e.g.: (win) ""file://c:\folder"", (linux) ""file:///usr/pub/files"")"); } }
        public static string DisplayName { get { return LC.L(@"Local folder or drive"); } }
        public static string AlternateDestinationMarkerLong(string optionname) { return LC.L(@"This option only works when the option --{0} is also specified. If there are alternate paths specified, this option indicates the name of a marker file that must be present in the folder. This can be used to handle situations where an external drive changes drive letter or mount point. By ensuring that a certain file exists, it is possible to prevent writing data to an unwanted external drive. The contents of the file are never examined, only file existence.", optionname); }
        public static string AlternateDestinationMarkerShort { get { return LC.L(@"Look for a file in the destination folder"); } }
        public static string AlternateTargetPathsLong(string optionname, char pathseparator) { return LC.L(@"This option allows multiple targets to be specified. The primary target path is placed before the list of paths supplied with this option. Before starting the backup, each folder in the list is checked for existence and optionally the presence of the marker file supplied by --{0}. The first existing path that optionally contains the marker file is then used as the destination. Multiple destinations are separated with a ""{1}"". On Windows, the path may be a UNC path, and the drive letter may be substituted with an asterisk (*), e.g: ""*:\backup"", which will examine all drive letters. If a username and password is supplied, the same credentials are used for all destinations.", optionname, pathseparator); }
        public static string AlternateTargetPathsShort { get { return LC.L(@"A list of secondary target paths"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string FolderMissingError(string foldername) { return LC.L(@"The folder {0} does not exist", foldername); }
        public static string NoDestinationWithMarkerFileError(string markername, string[] folders) { return LC.L(@"The marker file ""{0}"" was not found in any of the examined destinations: {1}", markername, string.Join(", ", folders)); }
        public static string UseMoveForPutLong { get { return LC.L(@"When storing the file, the standard operation is to copy the file and delete the original. This sequence ensures that the operation can be retried if something goes wrong. Activating this option may cause the retry operation to fail. This option has no effect unless the option --{0} is activated.", "disable-streaming-transfers"); } }
        public static string UseMoveForPutShort { get { return LC.L(@"Move the file instead of copying it"); } }
        public static string ForceReauthLong { get { return LC.L(@"If this option is set, any existing authentication against the remote share is dropped before attempting to authenticate."); } }
        public static string ForceReauthShort { get { return LC.L(@"Force authentication against remote share"); } }
        public static string DisableLengthVerificationLong { get { return LC.L(@"As an extra precaution the uploaded file length will be checked against the local source length."); } }
        public static string DisableLengthVerificationShort { get { return LC.L(@"Disable length verification"); } }
    }
}
