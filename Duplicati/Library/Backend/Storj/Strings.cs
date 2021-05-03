using Duplicati.Library.Localization.Short;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Strings
{
    internal static class Storj
    {
        public static string DisplayName { get { return LC.L(@"Storj DCS (Decentralized Cloud Storage)"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to the Storj DCS."); } }
        public static string TestConnectionFailed { get { return LC.L(@"The connection-test failed."); } }
        public static string StorjAuthMethodDescriptionShort { get { return LC.L(@"The authentication method"); } }
        public static string StorjAuthMethodDescriptionLong { get { return LC.L(@"The authentication method describes which way to use to connect to the network - either via API key or via an access grant."); } }
        public static string StorjSatelliteDescriptionShort { get { return LC.L(@"The satellite"); } }
        public static string StorjSatelliteDescriptionLong { get { return LC.L(@"The satellite that keeps track of all metadata. Use a Storj DCS server for high-performance SLA-backed connectivity or use a community server. Or even host your own."); } }
        public static string StorjAPIKeyDescriptionShort { get { return LC.L(@"The API key"); } }
        public static string StorjAPIKeyDescriptionLong { get { return LC.L(@"The API key grants access to a specific project on your chosen satellite. Head over to the dashboard of your satellite to create one if you do not already have an API key."); } }
        public static string StorjSecretDescriptionShort { get { return LC.L(@"The encryption passphrase"); } }
        public static string StorjSecretDescriptionLong { get { return LC.L(@"The encryption passphrase is used to encrypt your data before sending it to the Storj network. This passphrase can be the only secret to provide - for Storj you do not necessary need any additional encryption (from Duplicati) in place."); } }
        public static string StorjSecretVerifyDescriptionShort { get { return LC.L(@"The encryption passphrase (for verification)"); } }
        public static string StorjSecretVerifyDescriptionLong { get { return LC.L(@"The encryption passphrase verification to make sure you provided the expected value."); } }
        public static string StorjSharedAccessDescriptionShort { get { return LC.L(@"The access grant"); } }
        public static string StorjSharedAccessDescriptionLong { get { return LC.L(@"An access grant contains all information in one encrypted string. You may use it instead of a satellite, API key and secret."); } }
        public static string StorjBucketDescriptionShort { get { return LC.L(@"The bucket"); } }
        public static string StorjBucketDescriptionLong { get { return LC.L(@"The bucket where the backup will reside in."); } }
        public static string StorjFolderDescriptionShort { get { return LC.L(@"The folder"); } }
        public static string StorjFolderDescriptionLong { get { return LC.L(@"The folder within the bucket where the backup will reside in."); } }
        public static string StorjEncryptionPassphrasesDoNotMatchError { get { return LC.L(@"The encryption passphrases do not match"); } }
    }
}
