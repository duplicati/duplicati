using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class UploadData
    {
      [JsonProperty("max_file_size")]
      public long MaxFileSize { get; set; }

      [JsonProperty("nb_pipes")]
      public int NbPipes { get; set; }
    }
}
