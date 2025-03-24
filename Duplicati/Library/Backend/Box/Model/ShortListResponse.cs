using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class ShortListResponse : FileList
{
    [JsonProperty("order")] public OrderEntry[]? Order { get; set; }
}