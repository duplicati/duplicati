using Duplicati.Library.Localization.Short;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Strings
{
    internal static class Tardigrade
    {
        public static string DisplayName { get { return LC.L(@"Tardigrade Decentralized Cloud Storage"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to the Tardigrade Decentralized Cloud Storage."); } }
        public static string TardigradeAuthMethodDescriptionShort { get { return LC.L(@"The authentication method"); } }
        public static string TardigradeAuthMethodDescriptionLong { get { return LC.L(@"The authentication method describes which way to use to connect to the network - either via API key or via an access grant."); } }
        public static string TardigradeSatelliteDescriptionShort { get { return LC.L(@"The satellite"); } }
        public static string TardigradeSatelliteDescriptionLong { get { return LC.L(@"The satellite that keeps track of all metadata. Use a Tardigrade-grade server for high-performance SLA-backed connectivity or use a community server. Or even host your own."); } }
        public static string TardigradeAPIKeyDescriptionShort { get { return LC.L(@"The API key"); } }
        public static string TardigradeAPIKeyDescriptionLong { get { return LC.L(@"The API key grants access to a specific project on your chosen satellite. Head over to the dashboard of your satellite to create one if you do not already have an API key."); } }
        public static string TardigradeSecretDescriptionShort { get { return LC.L(@"The encryption passphrase"); } }
        public static string TardigradeSecretDescriptionLong { get { return LC.L(@"The encryption passphrase is used to encrypt your data before sending it to the tardigrade network. This passphrase can be the only secret to provide - for Tardigrade you do not necessary need any additional encryption (from Duplicati) in place."); } }
        public static string TardigradeSharedAccessDescriptionShort { get { return LC.L(@"The access grant"); } }
        public static string TardigradeSharedAccessDescriptionLong { get { return LC.L(@"An access grant contains all information in one encrypted string. You may use it instead of a satellite, API key and secret."); } }
        public static string TardigradeBucketDescriptionShort { get { return LC.L(@"The bucket"); } }
        public static string TardigradeBucketDescriptionLong { get { return LC.L(@"The bucket where the backup will reside in."); } }
        public static string TardigradeFolderDescriptionShort { get { return LC.L(@"The folder"); } }
        public static string TardigradeFolderDescriptionLong { get { return LC.L(@"The folder within the bucket where the backup will reside in."); } }
    }
}
