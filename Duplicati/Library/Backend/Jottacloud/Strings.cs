using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class Jottacloud
    {
        public static string DisplayName { get { return LC.L(@"Jottacloud"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Jottacloud using it's REST protocol. Allowed format is ""jottacloud://mountpoint/folder/subfolder""."); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string NoPathError { get { return LC.L(@"No path given, cannot upload files to the root folder"); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found, message: {1}", foldername, message); }
        public static string FileUploadError { get { return LC.L(@"Failed to upload file"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
    }
}
