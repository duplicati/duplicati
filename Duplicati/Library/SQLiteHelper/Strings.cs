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
namespace Duplicati.Library.SQLiteHelper.Strings {
    internal static class DatabaseUpgrader {
        public static string BackupFilenamePrefix { get { return LC.L(@"backup"); } }
        public static string DatabaseFormatError(string message) { return LC.L(@"Unable to determine database format: {0}", message); }
        public static string InvalidVersionError(int actualversion, int maxversion, string backupfolder) { return LC.L(@"
The database has version {0} but the largest supported version is {1}.

This is likely caused by upgrading to a newer version and then downgrading.
If this is the case, there is likely a backup file of the previous database version in the folder {2}.", actualversion, maxversion, backupfolder); }
        public static string TableLayoutError { get { return LC.L(@"Unknown table layout detected"); } }
        public static string UpgradeFailure(string sql, string message) { return LC.L(@"Failed to execute SQL: {0}
Error: {1}
Database is NOT upgraded.", sql, message); }
    }
}
