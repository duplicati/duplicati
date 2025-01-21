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
namespace Duplicati.Library.Snapshots.Strings {
    internal static class LinuxSnapshot {
        public static string ExternalProgramLaunchError(string message, string executable, string arguments) { return LC.L(@"The external command failed to start.
Error message: {0}
Command: {1} {2}", message, executable, arguments); }
        public static string ExternalProgramTimeoutError(string executable, string arguments) { return LC.L(@"The external command failed to complete within the set time limit: {0} {1}", executable, arguments); }
        public static string InvalidFilePathError(string localpath, string snapshotpath) { return LC.L(@"Unable to match local path {0} with any snapshot path: {1}", localpath, snapshotpath); }
        public static string MountFolderMissingError(string path, string message) { return LC.L(@"Script returned successfully, but the temporary folder {0} does not exist: {1}", path, message); }
        public static string MountFolderNotRemovedError(string path, string message) { return LC.L(@"Script returned successfully, but the temporary folder {0} still exist: {1}", path, message); }
        public static string ScriptExitCodeError(int actualcode, int expectedcode, string message) { return LC.L(@"The script returned exit code {0}, but {1} was expected: {2}", actualcode, expectedcode, message); }
        public static string ScriptOutputError(string parameter, string message) { return LC.L(@"Script returned successfully, but the output was missing the {0} parameter: {1}", parameter, message); }
    }
    internal static class USNHelper {
        public static string PathResolveError { get { return LC.L(@"Unable to determine full file path for USN entry"); } }
        public static string JournalEntriesDeleted { get { return LC.L(@"USN journal entries were purged since last scan"); } }
        public static string EmptyResponseError { get { return LC.L(@"Unexpected empty response while enumerating"); } }
        public static string LinuxNotSupportedError { get { return LC.L(@"USN is not supported on Linux"); } }
        public static string SafeGuardError { get { return LC.L(@"The number of files returned by USN was zero. This is likely an error. To remedy this, USN has been disabled."); } }
        public static string UnexpectedPathFormat { get { return LC.L(@"Unexpected path format encountered"); } }
        public static string UnsupportedUsnVersion { get { return LC.L(@"Unsupported USN journal version."); } }
        public static string PreviousBackupNoInfo { get { return LC.L(@"Previous backup did not record USN journal info"); } }
        public static string JournalIdChanged { get { return LC.L(@"USN journal ID changed"); } }
        public static string NextUsnZero { get { return LC.L(@"Next USN is zero"); } }
        public static string ConfigHashChanged { get { return LC.L(@"Backup configuration changed"); } }
    }
}
