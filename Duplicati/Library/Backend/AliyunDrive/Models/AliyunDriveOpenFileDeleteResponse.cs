using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 文件删除
    /// /adrive/v1.0/openFile/delete
    /// https://www.yuque.com/aliyundrive/zpfszx/get3mkr677pf10ws
    /// </summary>
    public class AliyunDriveOpenFileDeleteResponse
    {
        /// <summary>
        /// 云盘 ID
        /// </summary>
        [JsonProperty("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件 ID
        /// </summary>
        [JsonProperty("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 异步任务id，有的话表示需要经过异步处理。
        /// </summary>
        [JsonProperty("async_task_id")]
        public string AsyncTaskId { get; set; }
    }
}