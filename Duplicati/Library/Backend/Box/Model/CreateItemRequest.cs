using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class CreateItemRequest
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("parent")] public IDReference Parent { get; set; }
}