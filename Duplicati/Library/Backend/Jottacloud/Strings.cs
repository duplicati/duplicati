using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class Jottacloud
    {
        public static string DisplayName { get { return LC.L(@"Jottacloud"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Jottacloud using it's REST protocol. Allowed format is ""jottacloud://folder/subfolder""."); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string NoPathError { get { return LC.L(@"No path given, cannot upload files to the root folder"); } }
        public static string UnknownMountPoint { get { return LC.L(@"Illegal mount point given, only ""Archive"" and ""Sync"" are allowed."); } }
        public static string FileUploadError { get { return LC.L(@"Failed to upload file"); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionMountPointShort { get { return LC.L(@"Supplies the mount point to use on the server"); } }
        public static string DescriptionMountPointLong { get { return LC.L(@"The mount point to use on the server. The default is to use the ""Archive"" mount point, set this option to ""Sync"" to use the synchronization mount point instead."); } }
    }
}
