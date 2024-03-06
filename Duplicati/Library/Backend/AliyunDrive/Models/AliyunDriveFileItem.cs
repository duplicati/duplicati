using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 云盘文件信息
    /// Cloud Drive File Information
    /// </summary>
    public class AliyunDriveFileItem
    {
        /// <summary>
        /// 驱动器ID。
        /// Drive ID.
        /// </summary>
        [JsonProperty("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件ID。
        /// File ID.
        /// </summary>
        [JsonProperty("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 父文件夹ID。
        /// Parent Folder ID.
        /// </summary>
        [JsonProperty("parent_file_id")]
        public string ParentFileId { get; set; }

        /// <summary>
        /// 文件名。
        /// File name.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件夹名称（仅文件夹时有效）
        /// Folder name (only valid for folders).
        /// </summary>
        [JsonProperty("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// 文件大小。
        /// File size.
        /// </summary>
        [JsonProperty("size")]
        public long? Size { get; set; }

        /// <summary>
        /// 文件扩展名。
        /// File extension.
        /// </summary>
        [JsonProperty("file_extension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// 文件内容哈希。
        /// Content hash of the file.
        /// </summary>
        [JsonProperty("content_hash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// 文件内容哈希的算法名称，例如“SHA1”。
        /// Name of the algorithm for content hash, e.g., "SHA1".
        /// </summary>
        [JsonProperty("content_hash_name")]
        public string ContentHashName { get; set; }

        /// <summary>
        /// 文件分类。
        /// File category.
        /// </summary>
        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>
        /// 类型（文件或文件夹）
        /// Type (file or folder)
        /// file | folder
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// 是否为文件
        /// Whether it's a file.
        /// </summary>
        public bool IsFile => Type == "file";

        /// <summary>
        /// 是否为文件夹
        /// Whether it's a folder.
        /// </summary>
        public bool IsFolder => Type == "folder";

        /// <summary>
        /// 预览链接。
        /// Preview link.
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// 创建时间。
        /// Creation time.
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// Update time.
        /// </summary>
        [JsonProperty("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 文件的MIME类型，如'image/jpeg'、'application/pdf'等。
        /// MIME type of the file, such as 'image/jpeg', 'application/pdf', etc.
        /// </summary>
        [JsonProperty("mime_type")]
        public string MimeType { get; set; }

        /// <summary>
        /// 文件状态，例如“可用”、“已删除”等。
        /// File status, e.g., "available", "deleted", etc.
        /// available
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// 云盘列表信息
    /// Cloud Drive List Information
    /// </summary>
    public class AliyunFileList
    {
        /// <summary>
        /// 文件项列表。
        /// List of file items.
        /// </summary>
        [JsonProperty("items")]
        public List<AliyunDriveFileItem> Items { get; set; }

        /// <summary>
        /// 下一个分页标记。
        /// Next page marker.
        /// </summary>
        [JsonProperty("next_marker")]
        public string NextMarker { get; set; }
    }

}