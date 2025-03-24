using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class OrderEntry
{
    [JsonProperty("by")] public string? By { get; set; }
    [JsonProperty("direction")] public string? Direction { get; set; }
}