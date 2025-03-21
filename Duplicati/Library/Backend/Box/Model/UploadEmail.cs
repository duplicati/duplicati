using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class UploadEmail
{
    [JsonProperty("access")] public string Access { get; set; }
    [JsonProperty("email")] public string Email { get; set; }
}