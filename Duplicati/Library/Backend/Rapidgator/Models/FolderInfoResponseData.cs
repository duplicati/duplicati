using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class FolderInfoResponseData
    {
      [JsonProperty("folder")]
      public RapidgatorFolder Folder { get; set; }

      [JsonProperty("pager")]
      public Pager? Pager { get; set; }
    }
}
