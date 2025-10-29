using Duplicati.Library.Interface;
using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Model
{
    public class RapidgatorFile : IFileEntry
    {
      [JsonProperty("file_id")]
      public string FileId { get; set; }

      [JsonProperty("mode")]
      public int Mode { get; set; }

      [JsonProperty("mode_label")]
      public string ModeLabel { get; set; }

      [JsonProperty("folder_id")]
      public string FolderId { get; set; }

      [JsonProperty("name")]
      public string Name { get; set; }

      [JsonProperty("hash")]
      public string Hash { get; set; }

      [JsonProperty("size")]
      public long Size { get; set; }

      [JsonProperty("created")]
      public long Created { get; set; }

      [JsonProperty("url")]
      public string Url { get; set; }

      [JsonProperty("nb_downloads")]
      public int NbDownloads { get; set; }

      bool IFileEntry.IsFolder => false;

      DateTime IFileEntry.LastAccess => DateTimeOffset.FromUnixTimeSeconds(this.Created).UtcDateTime;

      DateTime IFileEntry.LastModification
      {
        get => DateTimeOffset.FromUnixTimeSeconds(this.Created).UtcDateTime;
      }

      bool IFileEntry.IsArchived => false;

      DateTime IFileEntry.Created => DateTimeOffset.FromUnixTimeSeconds(this.Created).UtcDateTime;
    }
}
