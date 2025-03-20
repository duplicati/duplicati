using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class MiniFolder : IDReference
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("etag")] public string ETag { get; set; }
    [JsonProperty("sequence_id")] public string SequenceID { get; set; }
}