using System;
using Duplicati.Library.Localization.Short;
using System.Collections.Generic;

namespace Duplicati.Library.Backend.Strings {
    internal static class TelegramBackend {
        public static string Description { get { return LC.L(@"This backend can read and write data to a Telegram backend."); } }
        public static string DisplayName { get { return LC.L(@"Telegram"); } }
        public static string ListVerifyFailure(string filename, IEnumerable<string> files) { return LC.L(@"The file {0} was uploaded but not found afterwards, the file listing returned {1}", filename, string.Join(Environment.NewLine, files)); }
        public static string ListVerifySizeFailure(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was uploaded but the returned size was {1} and it was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DescriptionDisableUploadVerifyShort { get { return LC.L(@"Disable upload verification"); } }
        public static string DescriptionDisableUploadVerifyLong { get { return LC.L(@"To protect against network failures, every upload will be attempted verified. Use this option to disable this verification to make the upload faster but less reliable."); } }
        public static string NoApiIdError { get { return LC.L(@"The API ID is missing."); } }
        public static string NoApiHashError { get { return LC.L(@"The API hash is missing."); } }
        public static string NoPhoneNumberError { get { return LC.L(@"The phone number is missing."); } }
    }
}
