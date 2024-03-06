using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 获取用户信息和drive信息
    /// Retrieve user information and drive information
    /// https://www.yuque.com/aliyundrive/zpfszx/mbb50w
    /// </summary>
    public class AliyunDriveInfo
    {
        /// <summary>
        /// 获取或设置用户ID，具有唯一性。
        /// Get or set the unique user ID.
        /// </summary>
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        /// <summary>
        /// 获取或设置用户昵称。
        /// Get or set the user's nickname.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 获取或设置用户头像地址。
        /// Get or set the user's avatar URL.
        /// </summary>
        [JsonProperty("avatar")]
        public string Avatar { get; set; }

        /// <summary>
        /// 获取或设置用户名。
        /// Get or set the username.
        /// </summary>
        [JsonProperty("user_name")]
        public string UserName { get; set; }

        /// <summary>
        /// 获取或设置用户昵称。
        /// Get or set the user's nickname.
        /// </summary>
        [JsonProperty("nick_name")]
        public string NickName { get; set; }

        /// <summary>
        /// 获取或设置默认 drive ID。
        /// Get or set the default drive ID.
        /// </summary>
        [JsonProperty("default_drive_id")]
        public string DefaultDriveId { get; set; }

        /// <summary>
        /// 获取或设置备份盘 drive ID。用户选择了授权才会返回。
        /// Get or set the backup drive ID. Returned only if the user has authorized.
        /// </summary>
        [JsonProperty("backup_drive_id")]
        public string BackupDriveId { get; set; }

        /// <summary>
        /// 获取或设置资源库 drive ID。用户选择了授权才会返回。
        /// Get or set the resource library drive ID. Returned only if the user has authorized.
        /// </summary>
        [JsonProperty("resource_drive_id")]
        public string ResourceDriveId { get; set; }

        /// <summary>
        /// 获取或设置相册 drive ID。如果没有相册 drive，可能为 null。
        /// Get or set the album drive ID. May be null if there is no album drive.
        /// </summary>
        [JsonProperty("album_drive_id")]
        public string AlbumDriveId { get; set; }
    }

}