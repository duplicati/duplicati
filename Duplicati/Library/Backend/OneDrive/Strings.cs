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
    internal static class MicrosoftGraph
    {
        public static string AuthIdLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string AuthIdShort { get { return LC.L(@"The authorization code"); } }
        public static string MissingAuthId(string url) { return LC.L(@"No Auth-ID was provided - you can get one from {0}", url); }
        public static string FragmentSizeLong { get { return LC.L(@"Size of individual fragments which are uploaded separately for large files. It is recommended to be between 5-10 MiB (though a smaller value may work better on a slower or less reliable connection), and to be a multiple of 320 KiB."); } }
        public static string FragmentSizeShort { get { return LC.L(@"Fragment size for large uploads"); } }
        public static string FragmentRetryCountLong { get { return LC.L(@"Number of retry attempts made for each fragment before failing the overall file upload."); } }
        public static string FragmentRetryCountShort { get { return LC.L(@"Number of retries for each fragment"); } }
        public static string FragmentRetryDelayLong { get { return LC.L(@"Amount of time (in milliseconds) to wait between failures when uploading fragments."); } }
        public static string FragmentRetryDelayShort { get { return LC.L(@"Millisecond delay between fragment errors"); } }
        public static string UseHttpClientLong { get { return LC.L(@"Use this option to set HttpClient class to perform HTTP requests."); } }
        public static string UseHttpClientShort { get { return LC.L(@"Whether the HttpClient class should be used"); } }
        public static string UseHttpClientDeprecated { get { return LC.L(@"The option --use-http-client is deprecated and has no effect."); } }
    }

    internal static class OneDrive
    {
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Store files in Microsoft OneDrive or Microsoft OneDrive for Business via the Microsoft Graph API. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3}).", mssadescription, mssalink, msopdescription, msoplink); }
        public static string DisplayName { get { return LC.L(@"Microsoft OneDrive"); } }
        public static string DriveIdLong(string defaultDrive) { return LC.L(@"ID of the drive to store data in. If no drive is specified, the default OneDrive or OneDrive for Business drive will be used via '{0}'.", defaultDrive); }
        public static string DriveIdShort { get { return LC.L(@"Optional ID of the drive"); } }
    }

    internal static class SharePointV2
    {
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Store files in a Microsoft SharePoint site via the Microsoft Graph API. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3}).", mssadescription, mssalink, msopdescription, msoplink); }
        public static string DisplayName { get { return LC.L(@"Microsoft SharePoint v2"); } }
        public static string SiteIdLong { get { return LC.L(@"ID of the site to store data in."); } }
        public static string SiteIdShort { get { return LC.L(@"ID of the site"); } }
        public static string MissingSiteId { get { return LC.L(@"No site ID was provided"); } }
        public static string ConflictingSiteId(string given, string found) { return LC.L(@"Conflicting site IDs used: given {0} but found {1}", given, found); }
    }

    internal static class MicrosoftGroup
    {
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Store files in a Microsoft Office 365 Group via the Microsoft Graph API. Allowed formats are ""sharepoint://tenant.sharepoint.com/{{PathToWeb}}//{{Documents}}/subfolder"" (with ""//"" being optionally used to indicate the root document folder) and ""sharepoint://subfolder"" (in which case you must also explicitly specify the SharePoint site's ID via --{0}). Usage of this backend requires that you agree to the terms in {1} ({2}) and {3} ({4}).", "site-id", mssadescription, mssalink, msopdescription, msoplink); }
        public static string DisplayName { get { return LC.L(@"Microsoft Office 365 Group"); } }
        public static string GroupIdLong { get { return LC.L(@"ID of the group to store data in."); } }
        public static string GroupIdShort { get { return LC.L(@"ID of the group"); } }
        public static string GroupEmailLong { get { return LC.L(@"Email address of the group to store data in."); } }
        public static string GroupEmailShort { get { return LC.L(@"Email address of the group"); } }
        public static string MissingGroupIdAndEmailAddress { get { return LC.L(@"No group ID or group email address was provided."); } }
        public static string NoGroupsWithEmail(string email) { return LC.L(@"No groups were found with the given email address: {0}", email); }
        public static string MultipleGroupsWithEmail(string email) { return LC.L(@"Multiple groups were found with the given email address: {0}", email); }
        public static string ConflictingGroupId(string given, string found) { return LC.L(@"Conflicting group IDs used: given {0} but found {1}", given, found); }
    }
}
