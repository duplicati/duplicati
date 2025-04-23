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

namespace Duplicati.Library.Encryption.Strings
{
    internal static class AESEncryption
    {
        public static string Description_v2 { get { return LC.L(@"This module encrypts all files in the same way that AESCrypt does, using 256 bit AES encryption."); } }
        public static string DisplayName { get { return LC.L(@"AES-256 encryption, built in"); } }
        public static string EmptyKeyError { get { return LC.L(@"Empty passphrase not allowed"); } }
        public static string AessetthreadlevelLong { get { return LC.L(@"Use this option to set the thread level allowed for AES crypt operations."); } }
        public static string AessetthreadlevelShort { get { return LC.L(@"Set thread level utilized for crypting"); } }
        public static string AessetthreadlevelDeprecated { get { return LC.L(@"The option --{0} is no longer used and has been deprecated.", "aes-set-threadlevel"); } }
    }
    internal static class EncryptionBase
    {
        public static string DecryptionError(string message) { return LC.L(@"Failed to decrypt data (invalid passphrase?): {0}", message); }
    }
    internal static class GPGEncryption
    {
        public static string Description { get { return LC.L(@"The GPG encryption module uses the GNU Privacy Guard program to encrypt and decrypt files. It requires that the gpg executable is available on the system. On Windows it is assumed that this is in the default installation folder under program files, under Linux and OSX it is assumed that the program is available via the PATH environment variable. It is possible to supply the path to GPG using the option --{0}.", "gpg-program-path"); } }
        public static string DisplayName { get { return LC.L(@"GNU Privacy Guard, external"); } }
        public static string GpgencryptiondecryptionswitchesLong { get { return LC.L(@"Use this switch to specify any extra options to GPG. You cannot specify the --passphrase-fd option here. The --decrypt option is always specified."); } }
        public static string GpgencryptiondecryptionswitchesShort { get { return LC.L(@"Extra GPG commandline options for decryption"); } }
        public static string GpgencryptionencryptionswitchesLong { get { return LC.L(@"Use this switch to specify any extra options to GPG. You cannot specify the --passphrase-fd option here. The --encrypt option is always specified."); } }
        public static string GpgencryptionencryptionswitchesShort { get { return LC.L(@"Extra GPG commandline options for encryption"); } }
        public static string GPGExecuteError(string program, string args, string message) { return LC.L(@"Failed to execute GPG with ""{0} {1}"": {2}", program, args, message); }
        public static string GpgprogrampathLong { get { return LC.L(@"The path to the GNU Privacy Guard program. If not supplied, Duplicati will search for ""gpg2"" and ""gpg"" on the system."); } }
        public static string GpgprogrampathShort { get { return LC.L(@"The path to GnuPG"); } }
        public static string GpgencryptionenablearmorLong { get { return LC.L(@"Use this option to supply the --armor option to GPG. The files will be larger but can be sent as pure text files."); } }
        public static string GpgencryptionenablearmorShort { get { return LC.L(@"Use GPG Armor"); } }
        public static string GpgencryptiondecryptioncommandLong { get { return LC.L(@"Override the GPG command supplied for decryption."); } }
        public static string GpgencryptiondecryptioncommandShort { get { return LC.L(@"The GPG decryption command"); } }
        public static string GpgencryptionencryptioncommandLong(string commandname, string optionvalue) { return LC.L(@"Override the default GPG encryption command ""{0}"". Normal usage is to request asymetric encryption with the setting {1}.", commandname, optionvalue); }
        public static string GpgencryptionencryptioncommandShort { get { return LC.L(@"The GPG encryption command"); } }
    }
    internal static class GPGStreamWrapper
    {
        public static string DecryptionError(string message) { return LC.L(@"Decryption failed: {0}", message); }
        public static string GPGFlushError { get { return LC.L(@"Failure while invoking GnuPG, program won't flush output"); } }
        public static string GPGTerminateError { get { return LC.L(@"Failure while invoking GnuPG, program won't terminate"); } }
    }
    internal static class EncryptedFieldHelper
    {
        public static string KeyTooShortError { get { return LC.L(@"Key must be at least 8 characters long"); } }
        public static string KeyEmptyError { get { return LC.L(@"Key must not be empty"); } }
        public static string KeyBlacklistedError { get { return LC.L(@"Refusing to encrypt with blacklisted key"); } }
    }
}
