using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class Pager
    {
      [JsonProperty("current")]
      public int Current { get; set; }

      [JsonProperty("total")]
      public int Total { get; set; }
    }
}
