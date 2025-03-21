using System;
using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class FileEntity : MiniFolder
{
    public FileEntity()
    {
        Size = -1;
    }

    [JsonProperty("sha1")] public string SHA1 { get; set; }

    [JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
    public long Size { get; set; }

    [JsonProperty("modified_at", NullValueHandling = NullValueHandling.Ignore)]
    public DateTime ModifiedAt { get; set; }
}