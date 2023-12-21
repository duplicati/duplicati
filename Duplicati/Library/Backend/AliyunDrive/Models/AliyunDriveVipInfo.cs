using Newtonsoft.Json;
using System;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 获取用户vip信息
    /// 表示API返回的用户VIP信息。
    /// https://www.yuque.com/aliyundrive/zpfszx/mbb50w
    /// </summary>
    public class AliyunDriveVipInfo
    {
        /// <summary>
        /// 获取或设置用户身份，可能的值包括：member, vip, svip。
        /// </summary>
        [JsonProperty("identity")]
        public string Identity { get; set; }

        /// <summary>
        /// 获取或设置用户等级，如：20t、8t。此字段可能为空。
        /// </summary>
        [JsonProperty("level")]
        public string Level { get; set; }

        /// <summary>
        /// 获取或设置VIP过期时间，为时间戳格式，单位秒。
        /// </summary>
        [JsonProperty("expire")]
        public long Expire { get; set; }

        /// <summary>
        /// 获取过期时间的 DateTime 表示形式。
        /// </summary>
        [JsonIgnore]
        public DateTime ExpireDateTime => DateTimeOffset.FromUnixTimeSeconds(Expire).DateTime;
    }
}