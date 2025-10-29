using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class ResponseData
    {
      [JsonProperty("token")]
      public string Token { get; set; }

      [JsonProperty("user")]
      public UserData User { get; set; }
    }
}
