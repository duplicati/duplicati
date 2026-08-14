using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend.Box;

public class ErrorResponse
{
    [JsonProperty("type")] public string? Type { get; set; }
    [JsonProperty("status")] public int Status { get; set; }
    [JsonProperty("code")] public string? Code { get; set; }
    [JsonProperty("help_url")] public string? HelpUrl { get; set; }
    [JsonProperty("message")] public string? Message { get; set; }
    [JsonProperty("request_id")] public string? RequestId { get; set; }
    [JsonProperty("context_info")] public ErrorContextInfo? ContextInfo { get; set; }

    /// <summary>
    /// Gets the items that already use the requested name, if the error reports any
    /// </summary>
    /// <returns>The conflicting items</returns>
    public IEnumerable<MiniFolder> GetConflictingItems()
    {
        var conflicts = ContextInfo?.Conflicts;
        if (conflicts == null)
            return [];

        var result = new List<MiniFolder>();
        foreach (var item in conflicts is JArray array ? array : [conflicts])
        {
            try
            {
                var parsed = item.ToObject<MiniFolder>();
                if (parsed != null)
                    result.Add(parsed);
            }
            catch (JsonException)
            {
                // Not an item description, so there is nothing to use here
            }
        }

        return result;
    }
}

public class ErrorContextInfo
{
    /// <summary>
    /// The items that already use the requested name.
    /// Box reports a list when creating a folder and a single item for file
    /// operations, so this is kept untyped and read through
    /// <see cref="ErrorResponse.GetConflictingItems"/>.
    /// </summary>
    [JsonProperty("conflicts")] public JToken? Conflicts { get; set; }
}
