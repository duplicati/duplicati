using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class UserData
    {
      [JsonProperty("email")]
      public string Email { get; set; }

      [JsonProperty("is_premium")]
      public bool IsPremium { get; set; }

      [JsonProperty("premium_end_time")]
      public long PremiumEndTime { get; set; }

      [JsonProperty("state")]
      public int State { get; set; }

      [JsonProperty("state_label")]
      public string StateLabel { get; set; }

      [JsonProperty("traffic")]
      public QuotaData Traffic { get; set; }

      [JsonProperty("storage")]
      public QuotaData Storage { get; set; }

      [JsonProperty("upload")]
      public UploadData Upload { get; set; }

      [JsonProperty("remote_upload")]
      public RemoteUploadData RemoteUpload { get; set; }
    }
}
