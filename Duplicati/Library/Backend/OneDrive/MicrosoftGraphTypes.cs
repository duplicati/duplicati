using System;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// Types are based on definitions from:
/// https://developer.microsoft.com/en-us/graph/docs/api-reference/v1.0/resources/onedrive
/// 
/// Note that some classes don't have the full set of properties defined, particularly if they don't seem like they are needed.
/// </summary>
namespace Duplicati.Library.Backend.MicrosoftGraph
{
    public class Identity
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }
    }

    public class IdentitySet
    {
        /// <summary>
        /// The optional application associated with this action
        /// </summary>
        [JsonProperty("application", NullValueHandling = NullValueHandling.Ignore)]
        public Identity Application { get; set; }

        /// <summary>
        /// The optional device associated with this action
        /// </summary>
        [JsonProperty("device", NullValueHandling = NullValueHandling.Ignore)]
        public Identity Device { get; set; }

        /// <summary>
        /// The optional user associated with this action
        /// </summary>
        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)]
        public Identity User { get; set; }
    }

    public class BaseItem
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        [JsonProperty("createdBy", NullValueHandling = NullValueHandling.Ignore)]
        public IdentitySet CreatedBy { get; set; }

        [JsonProperty("createdDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? CreatedDateTime { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("lastModifiedBy", NullValueHandling = NullValueHandling.Ignore)]
        public IdentitySet LastModifiedBy { get; set; }

        [JsonProperty("lastModifiedDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? LastModifiedDateTime { get; set; }

        /// <summary>
        /// Note: OneDrive and OneDrive for Business don't allow the following characters in file names:
        ///   &quot; * : &lt; &gt; ? / \ |
        /// https://support.office.com/en-us/article/Invalid-file-names-and-file-types-in-OneDrive-OneDrive-for-Business-and-SharePoint-64883a5d-228e-48f5-b3d2-eb39e07630fa
        /// If appears it also follows the Windows conventions for handling leading and trailing spaces,
        /// meaning the ASCII space character is trimmed off of both the front and back of the file name:
        /// https://support.microsoft.com/en-us/help/2829981/support-for-whitespace-characters-in-file-and-folder-names-for-windows
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("webUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string WebUrl { get; set; }
    }

    public enum QuotaState
    {
        /// <summary>
        /// The drive has plenty of remaining quota
        /// </summary>
        Normal,

        /// <summary>
        /// Remaining quota is under 10%
        /// </summary>
        Nearing,

        /// <summary>
        /// Remaining quota is under 1%
        /// </summary>
        Critical,

        /// <summary>
        /// No remaining quota - files can only be deleted and no new files can be added
        /// </summary>
        Exceeded,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        normal = Normal,
        nearing = Nearing,
        critical = Critical,
        exceeded = Exceeded,
    }

    public class Quota
    {
        [JsonProperty("total", NullValueHandling = NullValueHandling.Ignore)]
        public long Total { get; set; }

        [JsonProperty("used", NullValueHandling = NullValueHandling.Ignore)]
        public long Used { get; set; }

        [JsonProperty("remaining", NullValueHandling = NullValueHandling.Ignore)]
        public long Remaining { get; set; }

        [JsonProperty("deleted", NullValueHandling = NullValueHandling.Ignore)]
        public long Deleted { get; set; }

        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public QuotaState? State { get; set; }
    }

    public enum DriveType
    {
        /// <summary>
        /// OneDrive personal drives
        /// </summary>
        Personal,

        /// <summary>
        /// OneDrive for Business drives
        /// </summary>
        Business,

        /// <summary>
        /// SharePoint document libraries
        /// </summary>
        DocumentLibrary,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        personal = Personal,
        business = Business,
        documentLibrary = DocumentLibrary,
    }

    public class Drive : BaseItem
    {
        [JsonProperty("driveType", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public DriveType DriveType { get; set; }

        [JsonProperty("owner", NullValueHandling = NullValueHandling.Ignore)]
        public IdentitySet Owner { get; set; }

        [JsonProperty("quota", NullValueHandling = NullValueHandling.Ignore)]
        public Quota Quota { get; set; }
    }

    public class HashesType
    {
        /// <summary>
        /// SHA1 hash
        /// Available only in OneDrive personal
        /// </summary>
        [JsonProperty("sha1Hash", NullValueHandling = NullValueHandling.Ignore)]
        public string Sha1Hash { get; set; }

        /// <summary>
        /// CRC32 hash
        /// Available only in OneDrive personal
        /// </summary>
        [JsonProperty("crc32Hash", NullValueHandling = NullValueHandling.Ignore)]
        public string Crc32Hash { get; set; }

        /// <summary>
        /// A proprietary hash of the file that can be used to determine if the file's contents have changed.
        /// Available on in OneDrive for Business and SharePoint Server 2016.
        /// </summary>
        [JsonProperty("quickXorHash", NullValueHandling = NullValueHandling.Ignore)]
        public string QuickXorHash { get; set; }
    }

    public class FileFacet
    {
        [JsonProperty("hashes", NullValueHandling = NullValueHandling.Ignore)]
        public HashesType Hashes { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }

    public enum SortBy
    {
        Default = 0,

        /// <summary>
        /// Sorted by 'name' property
        /// </summary>
        Name,

        /// <summary>
        /// Sorted by type of item
        /// </summary>
        Type,

        /// <summary>
        /// Sorted by 'size' property
        /// </summary>
        Size,

        /// <summary>
        /// Sorted by 'takenDateTime' property of the photos facet, or 'createdDateTime' if that isn't available
        /// </summary>
        TakenOrCreatedDateTime,

        /// <summary>
        /// Sorted by 'lastModifiedDateTime' property
        /// </summary>
        LastModifiedDateTime,

        /// <summary>
        /// Sorted by a custom user specified sequence
        /// </summary>
        Sequence,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        @default = Default,
        name = Name,
        type = Type,
        size = Size,
        takenOrCreatedDateTime = TakenOrCreatedDateTime,
        lastModifiedDateTime = LastModifiedDateTime,
        sequence = Sequence,
    }

    public enum SortOrder
    {
        Ascending,
        Descending,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        ascending = Ascending,
        descending = Descending,
    }

    public enum ViewType
    {
        Default = 0,
        Icons,
        Details,
        Thumbnails,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        @default = Default,
        icons = Icons,
        details = Details,
        thumbnails = Thumbnails,
    }

    public class FolderView
    {
        [JsonProperty("sortBy", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SortBy? SortBy { get; set; }

        [JsonProperty("sortOrder", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SortOrder? SortOrder { get; set; }

        [JsonProperty("viewType", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public ViewType? ViewType { get; set; }
    }

    public class FolderFacet
    {
        [JsonProperty("childCount", NullValueHandling = NullValueHandling.Ignore)]
        public long? ChildCount { get; set; }

        [JsonProperty("view", NullValueHandling = NullValueHandling.Ignore)]
        public FolderView View { get; set; }
    }

    /// <summary>
    /// Note: These results are different from those in the core DriveItem type -
    /// they are the times the client reported from the local file system.
    /// For example, if a file is created locally on Monday, but uploaded on Tuesday,
    /// the DriveItem.CreatedDateTime will be Tuesday, but FileSystemInfoFacet.CreatedDateTime will be Monday.
    /// </summary>
    public class FileSystemInfoFacet
    {
        [JsonProperty("createdDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? CreatedDateTime { get; set; }

        /// <summary>
        /// This is not available in OneDrive for Business or SharePoint.
        /// </summary>
        [JsonProperty("lastAccessedDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? LastAccessedDateTime { get; set; }

        [JsonProperty("lastModifiedDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? LastModifiedDateTime { get; set; }
    }

    public class PackageFacet
    {
        /// <summary>
        /// Package type - currently only oneNote is defined, but others could be used.
        /// </summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

    public class DeletedFacet
    {
        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public string State { get; set; }
    }

    public class RemoteItemFacet
    {
        [JsonProperty("remoteItem", NullValueHandling = NullValueHandling.Ignore)]
        public RemoteItemFacet RemoteItem { get; set; }
    }

    public class RootFacet
    {
    }

    public class SpecialFolderFacet
    {
        /// <summary>
        /// The unique identifier which can be used under /drive/special/
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    /// <summary>
    /// To get an item from one of these references:
    /// GET https://graph.microsoft.com/v1.0/drives/{driveId}/items/{id}
    /// </summary>
    public class ItemReference
    {
        [JsonProperty("driveId", NullValueHandling = NullValueHandling.Ignore)]
        public string DriveId { get; set; }

        [JsonProperty("driveType", NullValueHandling = NullValueHandling.Ignore)]
        public DriveType DriveType { get; set; }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }
    }

    public enum ConflictBehavior
    {
        Fail,
        Replace,
        Rename,

        // Newer versions of JSON.NET add StringEnumCaseInsensitiveConverter, which can be used to serialize/deserialize enums
        // without respecting cases. Once we can use that, we can remove these copies of the nicely named enums.
        fail = Fail,
        replace = Replace,
        rename = Rename,
    }

    public class DriveItem : BaseItem
    {
        [JsonProperty("parentReference", NullValueHandling = NullValueHandling.Ignore)]
        public ItemReference ParentReference { get; set; }

        [JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
        public long? Size { get; set; }

        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)]
        public FileFacet File { get; set; }

        [JsonProperty("folder", NullValueHandling = NullValueHandling.Ignore)]
        public FolderFacet Folder { get; set; }

        [JsonProperty("fileSystemInfo", NullValueHandling = NullValueHandling.Ignore)]
        public FileSystemInfoFacet FileSystemInfo { get; set; }

        [JsonProperty("package", NullValueHandling = NullValueHandling.Ignore)]
        public PackageFacet Package { get; set; }

        [JsonProperty("deleted", NullValueHandling = NullValueHandling.Ignore)]
        public DeletedFacet Deleted { get; set; }

        [JsonProperty("remoteItem", NullValueHandling = NullValueHandling.Ignore)]
        public RemoteItemFacet RemoteItem { get; set; }

        [JsonProperty("root", NullValueHandling = NullValueHandling.Ignore)]
        public RootFacet Root { get; set; }

        [JsonProperty("specialFolder", NullValueHandling = NullValueHandling.Ignore)]
        public SpecialFolderFacet SpecialFolder { get; set; }

        [JsonProperty("@microsoft.graph.conflictBehavior", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public ConflictBehavior? ConflictBehavior { get; set; }

        [JsonProperty("@microsoft.graph.downloadUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string DownloadUrl { get; set; }

        [JsonIgnore]
        public bool IsFile
        {
            get
            {
                return this.File != null;
            }
        }

        [JsonIgnore]
        public bool IsFolder
        {
            get
            {
                return this.Folder != null;
            }
        }

        [JsonIgnore]
        public bool IsPackage
        {
            get
            {
                return this.Package != null;
            }
        }

        [JsonIgnore]
        public bool TreatLikeFolder
        {
            get
            {
                return this.IsFolder || this.IsPackage;
            }
        }

        [JsonIgnore]
        public bool IsDeleted
        {
            get
            {
                return this.Deleted != null;
            }
        }

        [JsonIgnore]
        public bool IsRemoteItem
        {
            get
            {
                return this.RemoteItem != null;
            }
        }

        [JsonIgnore]
        public bool IsRoot
        {
            get
            {
                return this.Root != null;
            }
        }

        [JsonIgnore]
        public bool IsSpecialFolder
        {
            get
            {
                return this.SpecialFolder != null;
            }
        }
    }

    public class GraphCollection<T>
    {
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public T[] Value { get; set; }

        [JsonProperty("@odata.nextLink", NullValueHandling = NullValueHandling.Ignore)]
        public string ODataNextLink { get; set; }
    }

    public class UploadSession
    {
        [JsonProperty("uploadUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string UploadUrl { get; set; }

        [JsonProperty("expirationDateTime", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? ExpirationDateTime { get; set; }

        [JsonProperty("nextExpectedRanges", NullValueHandling = NullValueHandling.Ignore)]
        public string[] NextExpectedRanges { get; set; }

        [JsonProperty("item", NullValueHandling = NullValueHandling.Ignore)]
        public DriveItem Item { get; set; }
    }

    public class SharePointSite : BaseItem
    {
    }

    public class Group : BaseItem
    {
        [JsonProperty("mail", NullValueHandling = NullValueHandling.Ignore)]
        public string Mail { get; set; }

        [JsonProperty("proxyAddresses", NullValueHandling = NullValueHandling.Ignore)]
        public string[] ProxyAddresses { get; set; }

        [JsonProperty("mailNickname", NullValueHandling = NullValueHandling.Ignore)]
        public string MailNickname { get; set; }

        [JsonProperty("groupTypes", NullValueHandling = NullValueHandling.Ignore)]
        public string[] GroupTypes { get; set; }

        [JsonIgnore]
        public bool IsUnifiedGroup
        {
            get
            {
                if (this.GroupTypes != null)
                {
                    return this.GroupTypes.Any(type => string.Equals("Unified", type, StringComparison.OrdinalIgnoreCase));
                }

                return false;
            }
        }
    }
}
