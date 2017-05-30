using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class SharePoint
    {
        public static string DisplayName { get { return LC.L(@"Microsoft SharePoint"); } }
        public static string Description { get { return LC.L(@"Supports connections to a SharePoint server (including OneDrive for Business). Allowed formats are ""mssp://tennant.sharepoint.com/PathToWeb//BaseDocLibrary/subfolder"" or ""mssp://username:password@tennant.sharepoint.com/PathToWeb//BaseDocLibrary/subfolder"". Use a double slash '//' in the path to denote the web from the documents library."); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionIntegratedAuthenticationLong { get { return LC.L(@"If the server and client both supports integrated authentication, this option enables that authentication method. This is likely only available with windows servers and clients."); } }
        public static string DescriptionIntegratedAuthenticationShort { get { return LC.L(@"Use windows integrated authentication to connect to the server"); } }
        public static string DescriptionUseRecyclerLong { get { return LC.L(@"Use this option to have files moved to the recycle bin folder instead of removing them permanently when compacting or deleting backups."); } }
        public static string DescriptionUseRecyclerShort { get { return LC.L(@"Move deleted files to the recycle bin"); } }

        public static string DescriptionBinaryDirectModeLong { get { return LC.L(@"Use this option to upload files to SharePoint as a whole with BinaryDirect mode. This is the most efficient way of uploading, but can cause non-recoverable timeouts under certain conditions. Use this option only with very fast and stable internet connections."); } }
        public static string DescriptionBinaryDirectModeShort { get { return LC.L(@"Upload files using binary direct mode."); } }

        public static string DescriptionWebTimeoutLong { get { return LC.L(@"Use this option to specify a custom value for timeouts of web operation when communicating with SharePoint Server. Recommended value is 180s."); } }
        public static string DescriptionWebTimeoutShort { get { return LC.L(@"Set timeout for SharePoint web operations."); } }

        public static string DescriptionChunkSizeLong { get { return LC.L(@"Use this option to specify the size of each chunk when uploading to SharePoint Server. Recommended value is 4MB."); } }
        public static string DescriptionChunkSizeShort { get { return LC.L(@"Set block size for chunked uploads to SharePoint."); } }

        public static string MissingElementError(string serverrelpath, string hosturl) { return LC.L(@"Element with path '{0}' not found on host '{1}'.", serverrelpath, hosturl); }
        public static string NoSharePointWebFoundError(string url) { return LC.L(@"No SharePoint web could be logged in to at path '{0}'. Maybe wrong credentials. Or try using '//' in path to separate web from folder path.", url); }
        public static string WebTitleReadFailedError { get { return LC.L(@"Everything seemed alright, but then web title could not be read to test connection. Something's wrong."); } }
    }

    internal static class OneDriveForBusiness
    {
        public static string DisplayName { get { return LC.L(@"Microsoft OneDrive for Business"); } }
        public static string Description { get { return LC.L(@"Supports connections to Microsoft OneDrive for Business. Allowed formats are ""od4b://tennant.sharepoint.com/personal/username_domain/Documents/subfolder"" or ""od4b://username:password@tennant.sharepoint.com/personal/username_domain/Documents/folder"". You can use a double slash '//' in the path to denote the base path from the documents folder."); } }
    }
}
