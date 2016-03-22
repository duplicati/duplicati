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
