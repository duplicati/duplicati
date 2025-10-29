using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class QuotaData
    {
      [JsonProperty("total")]
      public long Total { get; set; }

      [JsonProperty("left")]
      public long Left { get; set; }
    }
}
