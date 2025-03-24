using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class FileList
{
    [JsonProperty("total_count")] public long TotalCount { get; set; }
    [JsonProperty("entries")] public FileEntity[] Entries { get; set; }
    [JsonProperty("offset")] public long Offset { get; set; }
    [JsonProperty("limit")] public long Limit { get; set; }
}