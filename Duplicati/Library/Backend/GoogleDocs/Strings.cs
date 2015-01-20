using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class GoogleDocs {
        public static string CaptchaRequiredError(string url) { return LC.L(@"The account access has been blocked by Google, please visit this URL and unlock it: {0}", url); }
        public static string Description { get { return LC.L(@"This backend can read and write data to Google Docs. Supported format is ""googledocs://folder/subfolder""."); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionGoogleLabelsLong(string[] labels) { return LC.L(@"A comma separated list of labels to apply, known labels are: {0}", string.Join(",", labels)); }
        public static string DescriptionGoogleLabelsShort { get { return LC.L(@"A list of file labels"); } }
        public static string DescriptionGooglePasswordLong { get { return LC.L(@"This option supplies the password used to authenticate with Google. This can also be supplied through the ""auth-password"" option."); } }
        public static string DescriptionGooglePasswordShort { get { return LC.L(@"The Google account password"); } }
        public static string DescriptionGoogleUsernameLong { get { return LC.L(@"This option supplies the username used to authenticate with Google.  This can also be supplied through the ""auth-username"" property."); } }
        public static string DescriptionGoogleUsernameShort { get { return LC.L(@"The Google account username"); } }
        public static string Displayname { get { return LC.L(@"Google Docs"); } }
        public static string DuplicateFilenameFoundError(string filename, string foldername) { return LC.L(@"There are multiple files named ""{0}"" in the folder named ""{1}"", please rename the files manually", filename, foldername); }
        public static string DuplicateFoldernameFoundError(string foldername) { return LC.L(@"There are multiple folders named ""{0}"", please rename them manually", foldername); }
        public static string FileIsReadOnlyError(string filename) { return LC.L(@"The requested file is read-only: {0}", filename); }
        public static string FolderHasMultipleOwnersError(string foldername, string[] owners) { return LC.L(@"The folder {0} is owned by multiple parents: {1}, this is not supported", foldername, string.Join(", ", owners)); }
        public static string MissingPasswordError { get { return LC.L(@"No password supplied for Google Docs"); } }
        public static string MissingUsernameError { get { return LC.L(@"No username supplied for Google Docs"); } }
        public static string NoIDReturnedError { get { return LC.L(@"No resource ID found in response"); } }
        public static string NoResumeURLError { get { return LC.L(@"No resumeable upload url found"); } }
    }
}
