using Newtonsoft.Json;
using Duplicati.Library.Backend.Rapidgator.Model;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class RapidgatorFolder
    {
      [JsonProperty("folder_id")]
      public string FolderId { get; set; }

      [JsonProperty("mode")]
      public int Mode { get; set; }

      [JsonProperty("mode_label")]
      public string ModeLabel { get; set; }

      [JsonProperty("parent_folder_id")]
      public string? ParentFolderId { get; set; }

      [JsonProperty("name")]
      public string Name { get; set; }

      [JsonProperty("url")]
      public string Url { get; set; }

      [JsonProperty("nb_folders")]
      public int NbFolders { get; set; }

      [JsonProperty("nb_files")]
      public int NbFiles { get; set; }

      [JsonProperty("size_files")]
      public long SizeFiles { get; set; }

      [JsonProperty("created")]
      public long Created { get; set; }

      [JsonProperty("folders")]
      public List<RapidgatorFolder> Folders { get; set; }

      [JsonProperty("files")]
      public List<RapidgatorFile>? Files { get; set; }
    }
}
