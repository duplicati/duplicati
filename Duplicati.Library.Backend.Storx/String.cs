
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class StorxBackend
    {
        public static string DisplayName { get { return LC.L(@"StorX DCS(Decentralized Cloud Storage)"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Storx using REST API."); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
       
    }
}

