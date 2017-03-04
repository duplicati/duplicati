using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class MegaBackend {
        public static string DisplayName { get { return LC.L(@"mega.nz"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
        public static string NoPathError { get { return LC.L(@"No path given, cannot upload files to the root folder"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Mega.co.nz. Allowed formats are: ""mega://folder/subfolder"""); } }
    }
}
