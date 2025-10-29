using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    public class UploadResponseData
    {
      [JsonProperty("upload")]
      public RapidgatorUpload Upload { get; set; }
    }
}
