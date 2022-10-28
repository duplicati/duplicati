
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class StorxBackend
    {
        public static string DisplayName { get { return LC.L(@"StorX DCS(Decentralized Cloud Storage)"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Storx using REST API."); } }
        public static string NoSecretKeyError { get { return LC.L(@"No secretkey given"); } }
        public static string NoSecretMnemonicError { get { return LC.L(@"No secretmnemonic given"); } }
        public static string DescriptionAuthSecretkeyShort { get { return LC.L(@"Supplies the Secretkey used to connect to the server"); } }
        public static string DescriptionAuthSecretkeyLong { get { return LC.L(@"The Secretkey used to connect to the server. This may also be supplied as the environment variable ""AUTH_SECRETKEY""."); } }
        public static string DescriptionAuthSecretMnemonicShort { get { return LC.L(@"Supplies the SecretMnemonic used to connect to the server"); } }
        public static string DescriptionAuthSecretMnemonicLong { get { return LC.L(@"The SecretMnemonic used to connect to the server. This may also be supplied as the environment variable ""AUTH_SECRETMNEMONIC""."); } }
       
    }
}

