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
    internal static class GoogleCloudStorage
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Google Cloud Storage. Allowed format is ""gcs://bucket/folder""."); } }
        public static string DisplayName { get { return LC.L(@"Google Cloud Storage"); } }
        public static string ProjectIDMissingError(string projectoption) { return LC.L(@"You must supply a project ID with --{0} for creating a bucket.", projectoption); }
        public static string LocationDescriptionLong(string regions) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what region the data is stored in. Charges vary with bucket location. Known bucket locations:
{0}", regions); }
        public static string LocationDescriptionShort { get { return LC.L(@"Specify location option for creating a bucket"); } }
        public static string StorageclassDescriptionLong(string classes) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what storage type the bucket has. Charges and functionality vary with bucket storage class. Known storage classes:
{0}", classes); }
        public static string StorageclassDescriptionShort { get { return LC.L(@"Specify storage class for creating a bucket"); } }
        public static string ProjectDescriptionLong { get { return LC.L(@"This option is only used when creating new buckets. Use this option to supply the project ID that the bucket is attached to. The project determines where usage charges are applied."); } }
        public static string ProjectDescriptionShort { get { return LC.L(@"Specify project for creating a bucket"); } }
        public static string ServiceAccountJsonDescriptionShort { get { return LC.L(@"Service account JSON"); } }
        public static string ServiceAccountJsonDescriptionLong { get { return LC.L(@"String with JSON credentials for a Google Cloud service account. When set, AuthID is not required."); } }
        public static string ServiceAccountFileDescriptionShort { get { return LC.L(@"Service account JSON"); } }
        public static string ServiceAccountFileDescriptionLong { get { return LC.L(@"Path to a file with JSON credentials for a Google Cloud service account. When set, AuthID is not required."); } }
        public static string RetentionPolicyModeDescriptionShort { get { return LC.L(@"Specify retention policy mode"); } }
        public static string RetentionPolicyModeDescriptionLong { get { return LC.L(@"This option specifies the retention policy mode to use when setting object lock. Possible values are 'Locked' and 'Unlocked'."); } }
    }

    internal static class GoogleDrive
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Google Drive. Allowed format is ""googledrive://folder/subfolder""."); } }
        public static string DisplayName { get { return LC.L(@"Google Drive"); } }
        public static string MultipleEntries(string folder, string parent) { return LC.L(@"There is more than one item named ""{0}"" in the folder ""{1}"".", folder, parent); }
        public static string FilenotFound(string name) { return LC.L(@"File not found: {0}", name); }
        public static string TeamDriveIdLong { get { return LC.L("This option sets the team drive to use. Leaving it empty uses the personal drive."); } }
        public static string TeamDriveIdShort { get { return LC.L("Team drive ID"); } }
        public static string ListResponseError { get { return LC.L(@"The list response was not valid."); } }
        public static string AboutResponseError { get { return LC.L(@"The about response was not valid."); } }
        public static string CreateFolderResponseError { get { return LC.L(@"The create folder response was not valid."); } }
    }

    internal static class GCSConfig
    {
        public static string DisplayName => LC.L("Google Cloud Storage configuration module");
        public static string Description => LC.L("Expose Google Cloud Storage configuration as a web module");
        public static string ConfigTypeShort => LC.L("The config to get");
        public static string ConfigTypeLong => LC.L("Provide different config values");
    }
}
