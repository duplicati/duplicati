using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class RemoteUploadData
    {
      [JsonProperty("max_nb_jobs")]
      public int MaxNbJobs { get; set; }

      [JsonProperty("refresh_time")]
      public int RefreshTime { get; set; }
    }
}
