using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 获取用户空间信息
    /// https://www.yuque.com/aliyundrive/zpfszx/mbb50w
    /// </summary>
    public class AliyunDriveSpaceInfo
    {
        /// <summary>
        /// 获取或设置个人空间信息。
        /// </summary>
        [JsonProperty("personal_space_info")]
        public AliyunDrivePersonalSpaceInfo PersonalSpaceInfo { get; set; }
    }

    /// <summary>
    /// 表示用户的个人空间信息。
    /// </summary>
    public class AliyunDrivePersonalSpaceInfo
    {
        /// <summary>
        /// 获取或设置已使用的空间大小，单位为字节。
        /// </summary>
        [JsonProperty("used_size")]
        public long UsedSize { get; set; }

        /// <summary>
        /// 获取或设置总空间大小，单位为字节。
        /// </summary>
        [JsonProperty("total_size")]
        public long TotalSize { get; set; }
    }
}