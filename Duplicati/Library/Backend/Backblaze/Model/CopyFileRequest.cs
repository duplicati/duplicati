using Newtonsoft.Json;

namespace Duplicati.Library.Backend.Backblaze.Model
{
    public class CopyFileRequest
    {
        [JsonProperty("sourceFileId")]
        public string SourceFileId { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("metadataDirective")]
        public string MetadataDirective { get; set; } = "COPY";

        public CopyFileRequest(string sourceFileId, string fileName)
        {
            SourceFileId = sourceFileId;
            FileName = fileName;
        }
    }
}
