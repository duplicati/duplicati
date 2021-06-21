using Newtonsoft.Json;

namespace Duplicati.Library.IO.WindowsFileMetadata
{
    public class OwnerModel
    {
        [JsonProperty]
        public readonly string SID;
        [JsonProperty]
        public readonly string NTAccountName;

        public OwnerModel()
        {
        }

        public OwnerModel(System.Security.Principal.SecurityIdentifier identityReference)
        {
            SID = identityReference.Value;

            if (identityReference.Translate(typeof(System.Security.Principal.NTAccount)) is
                System.Security.Principal.NTAccount ntAccount)
            {
                NTAccountName = ntAccount.Value;
            }
        }
    }
}
