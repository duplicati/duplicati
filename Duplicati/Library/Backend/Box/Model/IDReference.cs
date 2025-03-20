using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class IDReference
{
    [JsonProperty("id")] public string ID { get; set; }
}