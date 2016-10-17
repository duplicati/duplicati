using System;
using System.Collections.Generic;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.AlternativeFTP
{
    internal static class Strings
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to an FTP based backend using an alternative FTP client. Allowed formats are ""aftp://hostname/folder"" or ""aftp://username:password@hostname/folder"""); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DisplayName { get { return LC.L(@"Alternative FTP"); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found. Message: {1}", foldername, message); }
        public static string ListVerifyFailure(string filename, IEnumerable<string> files) { return LC.L(@"The file {0} was uploaded but not found afterwards, the file listing returned {1}", filename, string.Join(Environment.NewLine, files)); }
        public static string ListVerifySizeFailure(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was uploaded but the returned size was {1} and it was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DescriptionDisableUploadVerifyShort { get { return LC.L(@"Disable upload verification"); } }
        public static string DescriptionDisableUploadVerifyLong { get { return LC.L(@"To protect against network or server failures, every upload will be attempted to be verified. Use this option to disable this verification to make the upload faster but less reliable."); } }
        public static string DescriptionFtpDataConnectionTypeLong { get { return LC.L(@"If this flag is set, the FTP data connection type will be changed to the selected option."); } }
        public static string DescriptionFtpDataConnectionTypeShort { get { return LC.L(@"Configure the FTP data connection type"); } }
        public static string DescriptionFtpEncryptionModeLong { get { return LC.L(@"If this flag is set, the FTP encryption mode will be changed to the selected option."); } }
        public static string DescriptionFtpEncryptionModeShort { get { return LC.L(@"Configure the FTP encryption mode"); } }
        public static string DescriptionSslProtocolsLong { get { return LC.L(@"This flag controls the SSL policy to use when encryption is enabled."); } }
        public static string DescriptionSslProtocolsShort { get { return LC.L(@"Configure the SSL policy to use when encryption is enabled"); } }
    }
}
