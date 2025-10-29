using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class DeleteResultData
    {
        [JsonProperty("result")]
        public DeleteResult Result { get; set; }
    }
}
