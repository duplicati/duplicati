// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class KeyGenerator
    {
        public static string Description => LC.L(@"Module for generating SSH private/public keys");
        public static string DisplayName => LC.L(@"SSH Key Generator");
        public static string KeyUsernameLong => LC.L(@"A username to append to the public key.");
        public static string KeyUsernameShort => LC.L(@"Public key username");
        public static string KeyTypeLong => LC.L(@"Determines the type of key to generate.");
        public static string KeyTypeShort => LC.L(@"The key type");
        public static string KeyLenLong => LC.L(@"The length of the key in bits.");
        public static string KeyLenShort => LC.L(@"The key length");
    }
    internal static class KeyUploader
    {
        public static string Description => LC.L(@"Module for uploading SSH public keys");
        public static string DisplayName => LC.L(@"SSH Key Uploader");
        public static string UrlLong => LC.L(@"The SSH connection URL used to establish the connection.");
        public static string UrlShort => LC.L(@"The SSH connection URL");
        public static string PubkeyLong => LC.L(@"The SSH public key must be a valid SSH string, which is appended to the .ssh/authorized_keys file.");
        public static string PubkeyShort => LC.L(@"The SSH public key to append");
    }
    internal static class SSHv2Backend
    {
        public static string Description => LC.L(@"This backend can read and write data to an SSH based backend, using SFTP. Allowed formats are ""ssh://hostname/folder"" and ""ssh://username:password@hostname/folder"".");
        public static string DisplayName => LC.L(@"SFTP (SSH)");
        public static string UsernameRequired => LC.L(@"A username is required");
        public static string PasswordRequired => LC.L(@"A password is required if not using a keyfile");
        public static string DescriptionFingerprintLong => LC.L(@"The server fingerprint used for validation of server identity. Format is e.g. ""ssh-rsa 4096 11:22:33:44:55:66:77:88:99:00:11:22:33:44:55:66"".");
        public static string DescriptionFingerprintShort => LC.L(@"Supply server fingerprint used for validation of server identity");
        public static string DescriptionAnyFingerprintLong => LC.L(@"To guard against man-in-the-middle attacks, the server fingerprint is verified on connection. Use this option to disable host-key fingerprint verification. You should only use this option for testing.");
        public static string DescriptionAnyFingerprintShort => LC.L(@"Disable fingerprint validation");
        public static string DescriptionSshkeyfileLong => LC.L(@"Point to a valid OpenSSH keyfile. If the file is encrypted, the password supplied is used to decrypt it. If the keyfile is specified, the password is not used to authenticate.");
        public static string DescriptionSshkeyfileShort => LC.L(@"Use a SSH private key to authenticate");
        public static string DescriptionSshkeyLong => LC.L(@"An url-encoded SSH private key. If the key is encrypted, the password supplied is used to decrypt it. If the private key is specified, the password is not used to authenticate.");
        public static string DescriptionSshkeyShort => LC.L(@"Use a SSH private key to authenticate");
        public static string DescriptionSshtimeoutLong => LC.L(@"Use this option to manage the internal timeout for SSH operations. If the value is set to zero, the operations will not time out.");
        public static string DescriptionSshtimeoutShort => LC.L(@"Set the operation timeout value");
        public static string DescriptionSshkeepaliveLong => LC.L(@"Use this option to enable the keep-alive interval for the SSH connection. If the connection is idle, aggressive firewalls might close the connection. Using keep-alive will keep the connection open in this scenario. If this value is set to zero, the keep-alive is disabled.");
        public static string DescriptionSshkeepaliveShort => LC.L(@"Set a keepalive value");
        public static string DescriptionDisableAgentShort => LC.L(@"Disable ssh-agent authentication");
        private static string MacOSAgentNotes => OperatingSystem.IsMacOS()
            ? LC.L(@"On macOS, the ssh-agent is used, and an attempt is made to use the keychain to load private key passphrases. If the private key itself is stored in the keychain, it will not be available.")
            : string.Empty;

        public static string DescriptionDisableAgentLong => LC.L(@$"Prevent using ssh-agent for authentication. When not set and no password or keyfile is supplied, the agent will be used. {MacOSAgentNotes}");
        public static string DescriptionRelativePathShort => LC.L(@"Treat source path as relative to the initial path");
        public static string DescriptionRelativePathLong => LC.L(@"Use this option to treat the source path as relative to the initial path. This is useful when the full path of the system is not known.");
        public static string FolderNotFoundManagedError(string foldername, string message) { return LC.L(@"Unable to set folder to {0}, error message: {1}", foldername, message); }
        public static string FingerprintNotMatchManagedError(string fingerprint) { return LC.L(@"Validation of server fingerprint failed. Server returned fingerprint ""{0}"". Cause of this message is either not correct configuration or Man-in-the-middle attack!", fingerprint); }
        public static string FingerprintNotSpecifiedManagedError(string fingerprint, string hostkeyoption, string allkeysoptions) { return LC.L(@"Please add --{1}=""{0}"" to trust this host. Optionally you can use --{2} (NOT SECURE) for testing!", fingerprint, hostkeyoption, allkeysoptions); }
        public static string TimeoutDeprecated(string self, string opt1, string opt2, string opt3) { return LC.L(@"The option {0} is deprecated. Please use --{1}, --{2} or --{3} instead.", self, opt1, opt2, opt3); }
    }
}
