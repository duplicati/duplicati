using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class ErrorResponse
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("status")] public int Status { get; set; }
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("help_url")] public string HelpUrl { get; set; }
    [JsonProperty("message")] public string Message { get; set; }
    [JsonProperty("request_id")] public string RequestId { get; set; }
}