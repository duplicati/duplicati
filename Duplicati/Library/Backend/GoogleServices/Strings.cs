//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USAusing Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class GoogleCloudStorage {        public static string Description { get { return LC.L(@"This backend can read and write data to Google Cloud Storage. Supported format is ""googlecloudstore://bucket/folder""."); } }        public static string DisplayName { get { return LC.L(@"Google Cloud Storage"); } }        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID, you can get it from: {0}"); }        public static string ProjectIDMissingError(string projectoption) { return LC.L(@"You must supply a project ID with --{0} for creating a bucket", projectoption); }        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }        public static string LocationDescriptionLong(string regions) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what region the data is stored in. Charges vary with bucket location. Known bucket locations:{0}", regions); }        public static string LocationDescriptionShort { get { return LC.L(@"Specifies location option for creating a bucket"); } }        public static string StorageclassDescriptionLong(string classes) { return LC.L(@"This option is only used when creating new buckets. Use this option to change what storage type the bucket has. Charges and functionality vary with bucket storage class. Known storage classes:{0}", classes); }        public static string StorageclassDescriptionShort { get { return LC.L(@"Specifies storage class for creating a bucket"); } }        public static string ProjectDescriptionShort { get { return LC.L(@"Specifies project for creating a bucket"); } }        public static string ProjectDescriptionLong { get { return LC.L(@"This option is only used when creating new buckets. Use this option to supply the project ID that the bucket is attached to. The project determines where usage charges are applied"); } }    }    internal static class GoogleDrive {        public static string CaptchaRequiredError(string url) { return LC.L(@"The account access has been blocked by Google, please visit this URL and unlock it: {0}", url); }        public static string Description { get { return LC.L(@"This backend can read and write data to Google Drive. Supported format is ""googledrive://folder/subfolder""."); } }        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }        public static string DisplayName { get { return LC.L(@"Google Drive"); } }        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID, you can get it from: {0}"); }        public static string MultipleEntries(string folder, string parent) { return LC.L(@"There is more than one item named ""{0}"" in the folder ""{1}"""); }    }}

