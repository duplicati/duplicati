using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Rapidgator.Models
{
    internal class DeleteResult
    {
        [JsonProperty("success")]
        public int Success { get; set; }

        [JsonProperty("success_ids")]
        public List<string> SuccessIds { get; set; }

        [JsonProperty("fail")]
        public int Fail { get; set; }

        [JsonProperty("fail_ids")]
        public List<string> FailIds { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }
    }
}
