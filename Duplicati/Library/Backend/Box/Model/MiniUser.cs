using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class MiniUser : IDReference
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("login")] public string Login { get; set; }
}