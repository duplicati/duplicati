using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class OneDrive {
        public static string AuthorizationFailure(string message, string url) { return LC.L(@"Failed to authorize using the WLID service: {0}. If the problem persists, try generating a new authid token from: {1}", message, url); }
        public static string AutoCreatedFolderLabel { get { return LC.L(@"Autocreated folder"); } }
        public static string UnexpectedError(System.Net.HttpStatusCode statuscode, string description) { return LC.L(@"Unexpected error code: {0} - {1}", statuscode, description); }
        public static string MissingFolderError(string folder) { return LC.L(@"Missing the folder: {0}", folder); }
        public static string FileNotFoundError(string name) { return LC.L(@"File not found: {0}", name); }
        public static string DisplayName { get { return LC.L(@"Microsoft OneDrive"); } }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Stores files on Microsoft OneDrive. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3})", mssadescription, mssalink, msopdescription, msoplink); }
    }

    internal static class MicrosoftGraph
    {
        public static string AuthIdShort { get { return OneDrive.AuthidShort; } }
        public static string AuthIdLong(string url) { return OneDrive.AuthidLong(url); }
        public static string MissingAuthId(string url) { return LC.L(@"No Auth-ID was provided - you can get one from {0}", url); }
        public static string FragmentSizeShort { get { return LC.L(@"Fragment size for large uploads"); } }
        public static string FragmentSizeLong { get { return LC.L(@"Size of individual fragments which are uploaded separately for large files. It is recommended to be between 5-10 MiB (though a smaller value may work better on a slower or less reliable connection), and to be a multiple of 320 KiB."); } }
        public static string FragmentRetryCountShort { get { return LC.L(@"Number of retries for each fragment"); } }
        public static string FragmentRetryCountLong { get { return LC.L(@"Number of retry attempts made for each fragment before failing the overall file upload"); } }
        public static string FragmentRetryDelayShort { get { return LC.L(@"Millisecond delay between fragment errors"); } }
        public static string FragmentRetryDelayLong { get { return LC.L(@"Amount of time (in milliseconds) to wait between failures when uploading fragments"); } }
    }

    internal static class OneDriveV2
    {
        public static string DisplayName { get { return LC.L(@"Microsoft OneDrive v2"); } }
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Stores files in Microsoft OneDrive or Microsoft OneDrive for Business via the Microsoft Graph API. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3})", mssadescription, mssalink, msopdescription, msoplink); }
        public static string DriveIdShort { get { return LC.L(@"Optional ID of the drive"); } }
        public static string DriveIdLong(string defaultDrive) { return LC.L(@"ID of the drive to store data in. If no drive is specified, the default OneDrive or OneDrive for Business drive will be used via '{0}'.", defaultDrive); }
    }

    internal static class SharePointV2
    {
        public static string DisplayName { get { return LC.L(@"Microsoft SharePoint v2"); } }
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Stores files in a Microsoft SharePoint site via the Microsoft Graph API. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3})", mssadescription, mssalink, msopdescription, msoplink); }
        public static string SiteIdShort { get { return LC.L(@"ID of the site"); } }
        public static string SiteIdLong { get { return LC.L(@"ID of the site to store data in"); } }
        public static string MissingSiteId { get { return LC.L(@"No site ID was provided"); } }
        public static string ConflictingSiteId(string given, string found) { return LC.L(@"Conflicting site IDs used: given {0} but found {1}", given, found); }
    }

    internal static class MicrosoftGroup
    {
        public static string DisplayName { get { return LC.L(@"Microsoft Office 365 Group"); } }
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Stores files in a Microsoft Office 365 Group via the Microsoft Graph API. Allowed formats are ""sharepoint://tenant.sharepoint.com/{{PathToWeb}}//{{Documents}}/subfolder"" (with ""//"" being optionally used to indicate the root document folder), or just ""sharepoint://subfolder"" (in which case you must also explicitly specify the SharePoint site's ID via --site-id). Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3})", mssadescription, mssalink, msopdescription, msoplink); }
        public static string GroupIdShort { get { return LC.L(@"ID of the group"); } }
        public static string GroupIdLong { get { return LC.L(@"ID of the group to store data in"); } }
        public static string MissingGroupId { get { return LC.L(@"No group ID was provided"); } }
        public static string NoGroupsWithEmail(string email) { return LC.L(@"No groups were found with the given email address: {0}", email); }
        public static string MultipleGroupsWithEmail(string email) { return LC.L(@"Multiple groups were found with the given email address: {0}", email); }
        public static string ConflictingGroupId(string given, string found) { return LC.L(@"Conflicting group IDs used: given {0} but found {1}", given, found); }
    }
}

