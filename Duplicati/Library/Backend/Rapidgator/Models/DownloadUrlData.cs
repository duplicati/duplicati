using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class DownloadUrlData
    {
      [JsonProperty("download_url")]
      public string DownloadUrl { get; set; }

      [JsonProperty("delay")]
      public int Delay { get; set; }
    }
}
