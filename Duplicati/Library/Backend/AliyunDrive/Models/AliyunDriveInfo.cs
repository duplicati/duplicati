using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 获取用户信息和drive信息
    /// https://www.yuque.com/aliyundrive/zpfszx/mbb50w
    /// </summary>
    public class AliyunDriveInfo
    {
        /// <summary>
        /// 获取或设置用户ID，具有唯一性。
        /// </summary>
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        /// <summary>
        /// 获取或设置用户昵称。
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 获取或设置用户头像地址。
        /// </summary>
        [JsonProperty("avatar")]
        public string Avatar { get; set; }

        /// <summary>
        /// 获取或设置用户名。
        /// </summary>
        [JsonProperty("user_name")]
        public string UserName { get; set; }

        /// <summary>
        /// 获取或设置用户昵称。
        /// </summary>
        [JsonProperty("nick_name")]
        public string NickName { get; set; }

        /// <summary>
        /// 获取或设置默认 drive ID。
        /// </summary>
        [JsonProperty("default_drive_id")]
        public string DefaultDriveId { get; set; }

        /// <summary>
        /// 获取或设置备份盘 drive ID。用户选择了授权才会返回。
        /// </summary>
        [JsonProperty("backup_drive_id")]
        public string BackupDriveId { get; set; }

        /// <summary>
        /// 获取或设置资源库 drive ID。用户选择了授权才会返回。
        /// </summary>
        [JsonProperty("resource_drive_id")]
        public string ResourceDriveId { get; set; }

        /// <summary>
        /// 获取或设置相册 drive ID。如果没有相册 drive，可能为 null。
        /// </summary>
        [JsonProperty("album_drive_id")]
        public string AlbumDriveId { get; set; }
    }
}