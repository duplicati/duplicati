using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class KeyGenerator {
        public static string Description { get { return LC.L(@"Module for generating SSH private/public keys"); } }
        public static string DisplayName { get { return LC.L(@"SSH Key Generator"); } }
        public static string KeyUsernameShort { get { return LC.L(@"Public key username"); } }
        public static string KeyUsernameLong { get { return LC.L(@"A username to append to the public key"); } }
        public static string KeyTypeShort { get { return LC.L(@"The key type"); } }
        public static string KeyTypeLong { get { return LC.L(@"Determines the type of key to generate"); } }
        public static string KeyLenShort { get { return LC.L(@"The key length"); } }
        public static string KeyLenLong { get { return LC.L(@"The length of the key in bits"); } }
    }
    internal static class KeyUploader {
        public static string Description { get { return LC.L(@"Module for uploading SSH public keys"); } }
        public static string DisplayName { get { return LC.L(@"SSH Key Uploader"); } }
        public static string UrlShort { get { return LC.L(@"The SSH connection URL"); } }
        public static string UrlLong { get { return LC.L(@"The SSH connection URL used to establish the connection"); } }
        public static string PubkeyShort { get { return LC.L(@"The SSH public key to append"); } }
        public static string PubkeyLong { get { return LC.L(@"The SSH public key must be a valid SSH string, which is appended to the .ssh/authorized_keys file"); } }
    }
    internal static class SSHv2Backend {
        public static string Description { get { return LC.L(@"This backend can read and write data to an SSH based backend, using SFTP. Allowed formats are ""ssh://hostname/folder"" or ""ssh://username:password@hostname/folder""."); } }
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }
        public static string DescriptionFingerprintLong { get { return LC.L(@"The server fingerprint used for validation of server identity. Format is eg. ""ssh-rsa 4096 11:22:33:44:55:66:77:88:99:00:11:22:33:44:55:66""."); } }
        public static string DescriptionFingerprintShort { get { return LC.L(@"Supplies server fingerprint used for validation of server identity"); } }
        public static string DescriptionAnyFingerprintLong { get { return LC.L(@"To guard against man-in-the-middle attacks, the server fingerprint is verified on connection. Use this option to disable host-key fingerprint verification. You should only use this option for testing."); } }
        public static string DescriptionAnyFingerprintShort { get { return LC.L(@"Disables fingerprint validation"); } }
        public static string DescriptionSshkeyfileLong { get { return LC.L(@"Points to a valid OpenSSH keyfile. If the file is encrypted, the password supplied is used to decrypt the keyfile.  If this option is supplied, the password is not used to authenticate. This option only works when using the managed SSH client."); } }
        public static string DescriptionSshkeyfileShort { get { return LC.L(@"Uses a SSH private key to authenticate"); } }
        public static string DescriptionSshkeyLong(string urlprefix) { return LC.L(@"An url-encoded SSH private key. The private key must be prefixed with {0}. If the file is encrypted, the password supplied is used to decrypt the keyfile.  If this option is supplied, the password is not used to authenticate. This option only works when using the managed SSH client.", urlprefix); }
        public static string DescriptionSshkeyShort { get { return LC.L(@"Uses a SSH private key to authenticate"); } }
        public static string DescriptionSshtimeoutLong { get { return LC.L(@"Use this option to manage the internal timeout for SSH operations. If this options is set to zero, the operations will not time out"); } }
		public static string DescriptionSshtimeoutShort { get { return LC.L(@"Sets the operation timeout value"); } }
		public static string DescriptionSshkeepaliveLong { get { return LC.L(@"This option can be used to enable the keep-alive interval for the SSH connection. If the connection is idle, aggressive firewalls might close the connection. Using keep-alive will keep the connection open in this scenario. If this value is set to zero, the keep-alive is disabled."); } }
		public static string DescriptionSshkeepaliveShort { get { return LC.L(@"Sets a keepalive value"); } }
		public static string DisplayName { get { return LC.L(@"SFTP (SSH)"); } }
        public static string FolderNotFoundManagedError(string foldername, string message) { return LC.L(@"Unable to set folder to {0}, error message: {1}", foldername, message); }
        public static string FingerprintNotMatchManagedError(string fingerprint) { return LC.L(@"Validation of server fingerprint failed. Server returned fingerprint ""{0}"". Cause of this message is either not correct configuration or Man-in-the-middle attack!", fingerprint); }
        public static string FingerprintNotSpecifiedManagedError(string fingerprint, string hostkeyoption, string allkeysoptions) { return LC.L(@"Please add --{1}=""{0}"" to trust this host. Optionally you can use --{2} (NOT SECURE) for testing!", fingerprint, hostkeyoption, allkeysoptions); }   
    }
}
