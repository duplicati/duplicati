using Newtonsoft.Json;
using Duplicati.Library.Backend.Rapidgator.Model;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class RapidgatorUpload
    {
      [JsonProperty("upload_id")]
      public string UploadId { get; set; }

      [JsonProperty("url")]
      public string Url { get; set; }

      [JsonProperty("file")]
      public RapidgatorFile File { get; set; }

      [JsonProperty("state")]
      public int State { get; set; }

      [JsonProperty("state_label")]
      public string StateLabel { get; set; }
    }
}
