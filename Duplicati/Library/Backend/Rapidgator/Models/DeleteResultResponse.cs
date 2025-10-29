using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class DeleteResultResponse
    {
      [JsonProperty("response")]
      public DeleteResultData Response { get; set; }

      [JsonProperty("status")]
      public HttpStatusCode Status { get; set; }

      [JsonProperty("details")]
      public object Details { get; set; }

      [JsonExtensionData]
      public Dictionary<string, JToken> AdditionalFields { get; set; }
    }
}
