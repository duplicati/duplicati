using System;
using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Box;

public class ListFolderResponse : MiniFolder
{
    [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }
    [JsonProperty("modified_at")] public DateTime ModifiedAt { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
    [JsonProperty("size")] public long Size { get; set; }
    [JsonProperty("path_collection")] public FolderList PathCollection { get; set; }
    [JsonProperty("created_by")] public MiniUser CreatedBy { get; set; }
    [JsonProperty("modified_by")] public MiniUser ModifiedBy { get; set; }
    [JsonProperty("owned_by")] public MiniUser OwnedBy { get; set; }
    [JsonProperty("shared_link")] public MiniUser SharedLink { get; set; }
    [JsonProperty("folder_upload_email")] public UploadEmail FolderUploadEmail { get; set; }
    [JsonProperty("parent")] public MiniFolder Parent { get; set; }
    [JsonProperty("item_status")] public string ItemStatus { get; set; }
    [JsonProperty("item_collection")] public FileList ItemCollection { get; set; }
}