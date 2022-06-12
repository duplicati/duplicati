using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class IDriveBackend
    {
        public static string DisplayName { get { return LC.L(@"IDrive"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string AuthTwoFactorKeyDescriptionShort { get { return LC.L(@"The shared secret used to generate two-factor TOTP codes."); } }
        public static string AuthTwoFactorKeyDescriptionLong { get { return LC.L(@"For accounts with two-factor authentication enabled, this is the shared secret used to generate the two-factor TOTP codes."); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to IDrive Sync. Allowed formats are: ""idrive://folder/subfolder"""); } }
    }
}