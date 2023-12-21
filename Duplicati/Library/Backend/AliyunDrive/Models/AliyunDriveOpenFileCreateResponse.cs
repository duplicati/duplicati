using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 文件上传 - 文件创建
    /// https://www.yuque.com/aliyundrive/zpfszx/ezlzok
    /// </summary>
    public class AliyunDriveOpenFileCreateResponse
    {
        /// <summary>
        /// 文件是否被删除。
        /// </summary>
        [JsonProperty("trashed")]
        public bool? Trashed { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件缩略图。
        /// </summary>
        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }

        /// <summary>
        /// 文件类型（如文件或文件夹）。
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// 文件分类。
        /// </summary>
        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>
        /// 文件是否隐藏。
        /// </summary>
        [JsonProperty("hidden")]
        public bool? Hidden { get; set; }

        /// <summary>
        /// 文件状态。
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// 文件描述。
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// 文件元数据。
        /// </summary>
        [JsonProperty("meta")]
        public string Meta { get; set; }

        /// <summary>
        /// 文件URL。
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// 文件大小。
        /// </summary>
        [JsonProperty("size")]
        public long? Size { get; set; }

        /// <summary>
        /// 文件是否加星标。
        /// </summary>
        [JsonProperty("starred")]
        public bool? Starred { get; set; }

        /// <summary>
        /// 文件是否可用。
        /// </summary>
        [JsonProperty("available")]
        public bool? Available { get; set; }

        /// <summary>
        /// 文件是否存在。
        /// </summary>
        [JsonProperty("exist")]
        public bool? Exist { get; set; }

        /// <summary>
        /// 用户标签。
        /// </summary>
        [JsonProperty("user_tags")]
        public string UserTags { get; set; }

        /// <summary>
        /// 文件MIME类型。
        /// </summary>
        [JsonProperty("mime_type")]
        public string MimeType { get; set; }

        /// <summary>
        /// 父文件ID。
        /// </summary>
        [JsonProperty("parent_file_id")]
        public string ParentFileId { get; set; }

        /// <summary>
        /// 驱动ID。
        /// </summary>
        [JsonProperty("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件ID。
        /// </summary>
        [JsonProperty("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 文件扩展名。
        /// </summary>
        [JsonProperty("file_extension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// 修订ID。
        /// </summary>
        [JsonProperty("revision_id")]
        public string RevisionId { get; set; }

        /// <summary>
        /// 内容哈希值。
        /// </summary>
        [JsonProperty("content_hash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// 内容哈希名称。
        /// </summary>
        [JsonProperty("content_hash_name")]
        public string ContentHashName { get; set; }

        /// <summary>
        /// 加密模式。
        /// </summary>
        [JsonProperty("encrypt_mode")]
        public string EncryptMode { get; set; }

        /// <summary>
        /// 域ID。
        /// </summary>
        [JsonProperty("domain_id")]
        public string DomainId { get; set; }

        /// <summary>
        /// 下载URL。
        /// </summary>
        [JsonProperty("download_url")]
        public string DownloadUrl { get; set; }

        /// <summary>
        /// 用户自定义元数据。
        /// </summary>
        [JsonProperty("user_meta")]
        public string UserMeta { get; set; }

        /// <summary>
        /// 内容类型。
        /// </summary>
        [JsonProperty("content_type")]
        public string ContentType { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// </summary>
        [JsonProperty("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 本地创建时间。
        /// </summary>
        [JsonProperty("local_created_at")]
        public DateTime? LocalCreatedAt { get; set; }

        /// <summary>
        /// 本地修改时间。
        /// </summary>
        [JsonProperty("local_modified_at")]
        public DateTime? LocalModifiedAt { get; set; }

        /// <summary>
        /// 删除时间。
        /// </summary>
        [JsonProperty("trashed_at")]
        public DateTime? TrashedAt { get; set; }

        /// <summary>
        /// 惩罚标志。
        /// </summary>
        [JsonProperty("punish_flag")]
        public bool? PunishFlag { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonProperty("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// 上传ID。
        /// </summary>
        [JsonProperty("upload_id")]
        public string UploadId { get; set; }

        /// <summary>
        /// 位置。
        /// </summary>
        [JsonProperty("location")]
        public string Location { get; set; }

        /// <summary>
        /// 是否快速上传。
        /// </summary>
        [JsonProperty("rapid_upload")]
        public bool RapidUpload { get; set; }

        /// <summary>
        /// 分片信息列表。
        /// </summary>
        [JsonProperty("part_info_list")]
        public List<AliyunDriveOpenFileCreatePartInfo> PartInfoList { get; set; }

        /// <summary>
        /// 流上传信息。
        /// </summary>
        [JsonProperty("streams_upload_info")]
        public string StreamsUploadInfo { get; set; }

        /// <summary>
        /// 分片信息类。
        /// </summary>
        public class AliyunDriveOpenFileCreatePartInfo
        {
            /// <summary>
            /// ETag。
            /// </summary>
            [JsonProperty("etag")]
            public string Etag { get; set; }

            /// <summary>
            /// 分片编号。
            /// </summary>
            [JsonProperty("part_number")]
            public int? PartNumber { get; set; }

            /// <summary>
            /// 分片大小。
            /// </summary>
            [JsonProperty("part_size")]
            public long? PartSize { get; set; }

            /// <summary>
            /// 上传URL。
            /// </summary>
            [JsonProperty("upload_url")]
            public string UploadUrl { get; set; }

            /// <summary>
            /// 内容类型。
            /// </summary>
            [JsonProperty("content_type")]
            public string ContentType { get; set; }

            /// <summary>
            /// 上传表单信息。
            /// </summary>
            [JsonProperty("upload_form_info")]
            public string UploadFormInfo { get; set; }
        }
    }
}
