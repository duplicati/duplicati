using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class FolderInfoResponse
    {
      [JsonProperty("response")]
      public FolderInfoResponseData Response { get; set; }

      [JsonProperty("status")]
      public HttpStatusCode Status { get; set; }

      [JsonProperty("details")]
      public object? Details { get; set; }

      [JsonExtensionData]
      public Dictionary<string, JToken> AdditionalFields { get; set; }
    }
}
