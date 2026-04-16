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
namespace Duplicati.Library.Backend.MovistarCloud.Strings
{
    internal static class MovistarCloudBackend
    {
        public static string Description => LC.L(@"This backend can read and write data to Movistar Cloud (MiCloud/Zefiro) using its REST protocol. Supported format is ""movistarcloud://folder/subfolder"".");
        public static string DisplayName => LC.L(@"Movistar Cloud (Unofficial)");
        public static string EmailShort => LC.L(@"MiCloud account email");
        public static string EmailLong => LC.L(@"The email address for the MiCloud account used for login.");
        public static string PasswordShort => LC.L(@"MiCloud account password");
        public static string PasswordLong => LC.L(@"The password for the MiCloud account used for login.");
        public static string ClientIdShort => LC.L(@"MiCloud client ID");
        public static string ClientIdLong => LC.L(@"The MiCloud client ID. Obtain this from a web session by checking the developer tools in your browser.");
        public static string RootFolderPathShort => LC.L(@"Root folder path");
        public static string RootFolderPathLong => LC.L(@"The path where to store the backup (e.g., /Duplicati/Backups/Computer).");
        public static string ListLimitShort => LC.L(@"List limit");
        public static string ListLimitLong => LC.L(@"The maximum number of items returned per listing call.");
        public static string WaitForValidationShort => LC.L(@"Wait for validation");
        public static string WaitForValidationLong => LC.L(@"Wait until the uploaded file becomes usable (status=U).");
        public static string ValidationTimeoutShort => LC.L(@"Validation timeout");
        public static string ValidationTimeoutLong => LC.L(@"The maximum time to wait for server-side upload validation.");
        public static string ValidationPollIntervalShort => LC.L(@"Validation poll interval");
        public static string ValidationPollIntervalLong => LC.L(@"The polling interval for checking validation status.");
        public static string DiagnosticsShort => LC.L(@"Enable diagnostics");
        public static string DiagnosticsLong => LC.L(@"Enable diagnostics logging during TestAsync to log storage space and trash information.");
        public static string DiagnosticsLevelShort => LC.L(@"Diagnostics level");
        public static string DiagnosticsLevelLong => LC.L(@"The level of diagnostics to log. Basic logs storage space only, Trash also logs trash entries.");
        public static string TrashPageSizeShort => LC.L(@"Trash page size");
        public static string TrashPageSizeLong => LC.L(@"The number of items to list from trash when diagnostics-level is set to Trash.");
    }
}
