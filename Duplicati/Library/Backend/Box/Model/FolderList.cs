using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class FolderList
{
    [JsonProperty("total_count")] public long TotalCount { get; set; }
    [JsonProperty("entries")] public MiniFolder[]? Entries { get; set; }
}