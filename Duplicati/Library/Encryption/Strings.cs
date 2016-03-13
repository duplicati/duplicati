using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Encryption.Strings {
    internal static class AESEncryption {
        public static string Description_v2 { get { return LC.L(@"This module encrypts all files in the same way that AESCrypt does, using 256 bit AES encryption."); } }
        public static string DisplayName { get { return LC.L(@"AES-256 encryption, built in"); } }
        public static string EmptyKeyError { get { return LC.L(@"Blank or empty passphrase not allowed"); } }
        public static string AessetthreadlevelLong { get { return LC.L(@"Use this option to set the thread level allowed for AES crypt operations. Valid values are 0 (uses default), or from 1 (no multithreading) to 4 (max. multithreading)"); } }
        public static string AessetthreadlevelShort { get { return LC.L(@"Set thread level utilized for crypting (0-4)"); } }
    }
    internal static class EncryptionBase {
        public static string DecryptionError(string message) { return LC.L(@"Failed to decrypt data (invalid passphrase?): {0}", message); }
    }
    internal static class GPGEncryption {
        public static string Description { get { return LC.L(@"The GPG encryption module uses the GNU Privacy Guard program to encrypt and decrypt files. It requires that the gpg executable is available on the system. On Windows it is assumed that this is in the default installation folder under program files, under Linux and OSX it is assumed that the program is available via the PATH environment variable. It is possible to supply the path to GPG using the --gpg-program-path switch."); } }
        public static string DisplayName { get { return LC.L(@"GNU Privacy Guard, external"); } }
        public static string GpgencryptiondecryptionswitchesLong { get { return LC.L(@"Use this switch to specify any extra options to GPG. You cannot specify the --passphrase-fd option here. The --decrypt option is always specified."); } }
        public static string GpgencryptiondecryptionswitchesShort { get { return LC.L(@"Extra GPG commandline options for decryption"); } }
        public static string GpgencryptiondisablearmorLong { get { return LC.L(@"The GPG encryption/decryption will use the --armor option for GPG to protect the files with armor. Specify this switch to remove the --armor option."); } }
        public static string GpgencryptiondisablearmorShort { get { return LC.L(@"Don't use GPG Armor"); } }
        public static string GpgencryptionencryptionswitchesLong { get { return LC.L(@"Use this switch to specify any extra options to GPG. You cannot specify the --passphrase-fd option here. The --encrypt option is always specified."); } }
        public static string GpgencryptionencryptionswitchesShort { get { return LC.L(@"Extra GPG commandline options for encryption"); } }
        public static string GPGExecuteError(string program, string args, string message) { return LC.L(@"Failed to execute GPG at """"{0}"" {1}"": {2}", program, args, message); }
        public static string GpgprogrampathLong { get { return LC.L(@"The path to the GNU Privacy Guard program. If not supplied, Duplicati will assume that the program ""gpg"" is available in the system path."); } }
        public static string GpgprogrampathShort { get { return LC.L(@"The path to GnuPG"); } }
        public static string Gpgencryptiondisablearmordeprecated(string optionname) { return LC.L(@"This option has non-standard handling, please use the --{0} option instead.", optionname); }
        public static string GpgencryptionenablearmorLong { get { return LC.L(@"Use this option to supply the --armor option to GPG. The files will be larger but can be sent as pure text files."); } }
        public static string GpgencryptionenablearmorShort { get { return LC.L(@"Use GPG Armor"); } }
        public static string GpgencryptiondecryptioncommandLong { get { return LC.L(@"Overrides the GPG command supplied for decryption"); } }
        public static string GpgencryptiondecryptioncommandShort { get { return LC.L(@"The GPG decryption command"); } }
        public static string GpgencryptionencryptioncommandLong(string commandname, string optionvalue) { return LC.L(@"Overrides the default GPG encryption command ""{0}"", normal usage is to request asymetric encryption with the setting {1}", commandname, optionvalue); }
        public static string GpgencryptionencryptioncommandShort { get { return LC.L(@"The GPG encryption command"); } }
    }
    internal static class GPGStreamWrapper {
        public static string DecryptionError(string message) { return LC.L(@"Decryption failed: {0}", message); }
        public static string GPGFlushError { get { return LC.L(@"Failure while invoking GnuPG, program won't flush output"); } }
        public static string GPGTerminateError { get { return LC.L(@"Failure while invoking GnuPG, program won't terminate"); } }
    }
}
