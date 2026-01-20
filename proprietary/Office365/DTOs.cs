// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duplicati.Proprietary.Office365;

internal sealed class GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }

    [JsonPropertyName("accountEnabled")]
    public bool? AccountEnabled { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

}

internal sealed class GraphSite
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("siteCollection")]
    public GraphSiteCollection? SiteCollection { get; set; }
}

internal sealed class GraphSiteCollection
{
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("personalSite")]
    public bool? PersonalSite { get; set; }
}

internal sealed class OfficeTokenResponse
{
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    [JsonPropertyName("expires_on")]
    public string ExpiresOn { get; set; } = string.Empty;
}

internal sealed class GraphPage<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

internal sealed class GraphMailFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }

    [JsonPropertyName("childFolderCount")]
    public int? ChildFolderCount { get; set; }

    [JsonPropertyName("totalItemCount")]
    public int? TotalItemCount { get; set; }

    [JsonPropertyName("unreadItemCount")]
    public int? UnreadItemCount { get; set; }

    // Indicates whether the folder is hidden (not returned by default when listing)
    [JsonPropertyName("isHidden")]
    public bool? IsHidden { get; set; }
}

internal sealed class GraphMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("receivedDateTime")]
    public DateTimeOffset? ReceivedDateTime { get; set; }

    [JsonPropertyName("sentDateTime")]
    public DateTimeOffset? SentDateTime { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("internetMessageId")]
    public string? InternetMessageId { get; set; }

    [JsonPropertyName("from")]
    public GraphRecipient? From { get; set; }

    [JsonPropertyName("toRecipients")]
    public List<GraphRecipient>? ToRecipients { get; set; }

    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; set; }

    [JsonPropertyName("body")]
    public GraphBody? Body { get; set; }

    [JsonPropertyName("sender")]
    public GraphRecipient? Sender { get; set; }

    [JsonPropertyName("ccRecipients")]
    public List<GraphRecipient>? CcRecipients { get; set; }

    [JsonPropertyName("bccRecipients")]
    public List<GraphRecipient>? BccRecipients { get; set; }
}

internal sealed class GraphBody
{
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; } // "Text" or "HTML"

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class GraphAttachment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; } // "#microsoft.graph.fileAttachment"

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    public int? Size { get; set; }

    [JsonPropertyName("isInline")]
    public bool? IsInline { get; set; }

    [JsonPropertyName("contentId")]
    public string? ContentId { get; set; }

    // For fileAttachment
    [JsonPropertyName("contentBytes")]
    public string? ContentBytes { get; set; } // Base64
}

internal sealed class GraphAttachmentItem
{
    [JsonPropertyName("attachmentType")]
    public string? AttachmentType { get; set; } // "file"

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

internal sealed class GraphUploadSession
{
    [JsonPropertyName("uploadUrl")]
    public string? UploadUrl { get; set; }

    [JsonPropertyName("expirationDateTime")]
    public DateTimeOffset? ExpirationDateTime { get; set; }

    [JsonPropertyName("nextExpectedRanges")]
    public List<string>? NextExpectedRanges { get; set; }
}

internal sealed class GraphRecipient
{
    [JsonPropertyName("emailAddress")]
    public GraphEmailAddress? EmailAddress { get; set; }
}

internal sealed class GraphEmailAddress
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

internal sealed class GraphContact
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("emailAddresses")]
    public List<GraphContactEmail>? EmailAddresses { get; set; }

    [JsonPropertyName("mobilePhone")]
    public string? MobilePhone { get; set; }

    [JsonPropertyName("businessPhones")]
    public List<string>? BusinessPhones { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }
}

internal sealed class GraphContactEmail
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

internal sealed class GraphContactFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }
}

internal sealed class GraphContactGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}

internal sealed class GraphTodoTaskList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("isOwner")]
    public bool? IsOwner { get; set; }

    [JsonPropertyName("isShared")]
    public bool? IsShared { get; set; }

    // none, defaultList, flaggedEmails, unknownFutureValue
    [JsonPropertyName("wellknownListName")]
    public string? WellknownListName { get; set; }
}

internal sealed class GraphNotebook
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; set; }
}

internal sealed class GraphOnenoteSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}

internal sealed class GraphOnenoteSectionGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}

internal sealed class GraphOnenotePage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }
}

internal sealed class GraphTodoTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("importance")]
    public string? Importance { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("completedDateTime")]
    public object? CompletedDateTime { get; set; }

    [JsonPropertyName("dueDateTime")]
    public object? DueDateTime { get; set; }

    [JsonPropertyName("body")]
    public object? Body { get; set; }
}

internal sealed class GraphTodoChecklistItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("isChecked")]
    public bool? IsChecked { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }
}

internal sealed class GraphTodoLinkedResource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }
}

internal sealed class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("eTag")]
    public string? ETag { get; set; }

    [JsonPropertyName("cTag")]
    public string? CTag { get; set; }

    [JsonPropertyName("parentReference")]
    public GraphDriveItemParentReference? ParentReference { get; set; }

    // Present in delta results when deleted
    [JsonPropertyName("deleted")]
    public JsonElement? Deleted { get; set; }

    // Facets (presence indicates item type)
    [JsonPropertyName("folder")]
    public GraphDriveFolderFacet? Folder { get; set; }

    [JsonPropertyName("file")]
    public GraphDriveFileFacet? File { get; set; }

    [JsonPropertyName("fileSystemInfo")]
    public GraphDriveFileSystemInfo? FileSystemInfo { get; set; }

    // Often useful when downloading
    [JsonPropertyName("@microsoft.graph.downloadUrl")]
    public string? DownloadUrl { get; set; }
}

internal sealed class GraphDriveFolderFacet
{
    [JsonPropertyName("childCount")]
    public int? ChildCount { get; set; }
}

internal sealed class GraphDriveFileFacet
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("hashes")]
    public GraphDriveHashes? Hashes { get; set; }
}

internal sealed class GraphDriveHashes
{
    [JsonPropertyName("quickXorHash")]
    public string? QuickXorHash { get; set; }

    [JsonPropertyName("sha1Hash")]
    public string? Sha1Hash { get; set; }

    [JsonPropertyName("sha256Hash")]
    public string? Sha256Hash { get; set; }
}

internal sealed class GraphDriveFileSystemInfo
{
    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}

internal sealed class GraphDriveItemParentReference
{
    [JsonPropertyName("driveId")]
    public string? DriveId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

internal sealed class GraphDeltaPage<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }

    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaLink { get; set; }
}

internal sealed record GraphDeltaResult<T>(List<T> Items, string DeltaLink);

internal sealed class GraphDrive
{
    // baseItem
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("createdBy")]
    public object? CreatedBy { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    public object? LastModifiedBy { get; set; }

    // drive
    [JsonPropertyName("driveType")]
    public string? DriveType { get; set; }

    [JsonPropertyName("owner")]
    public object? Owner { get; set; }

    [JsonPropertyName("quota")]
    public object? Quota { get; set; }

    [JsonPropertyName("sharepointIds")]
    public object? SharePointIds { get; set; }

    [JsonPropertyName("system")]
    public object? System { get; set; }
}

internal sealed class GraphCalendarGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("classId")]
    public string? ClassId { get; set; }

    [JsonPropertyName("changeKey")]
    public string? ChangeKey { get; set; }
}

internal sealed class GraphCalendar
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("changeKey")]
    public string? ChangeKey { get; set; }

    // Graph: calendarColor enum (kept as string for forward compatibility)
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    // Graph: hexColor string (e.g. "#FF0000")
    [JsonPropertyName("hexColor")]
    public string? HexColor { get; set; }

    [JsonPropertyName("isDefaultCalendar")]
    public bool? IsDefaultCalendar { get; set; }

    [JsonPropertyName("isRemovable")]
    public bool? IsRemovable { get; set; }

    [JsonPropertyName("canEdit")]
    public bool? CanEdit { get; set; }

    [JsonPropertyName("canShare")]
    public bool? CanShare { get; set; }

    [JsonPropertyName("canViewPrivateItems")]
    public bool? CanViewPrivateItems { get; set; }

    [JsonPropertyName("isTallyingResponses")]
    public bool? IsTallyingResponses { get; set; }

    // Values like: unknown, skypeForBusiness, skypeForBusinessForTeams, teamsForBusiness
    [JsonPropertyName("defaultOnlineMeetingProvider")]
    public string? DefaultOnlineMeetingProvider { get; set; }

    [JsonPropertyName("allowedOnlineMeetingProviders")]
    public List<string>? AllowedOnlineMeetingProviders { get; set; }

    [JsonPropertyName("owner")]
    public GraphEmailAddress? Owner { get; set; }
}

internal sealed class GraphEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    // Graph uses dateTimeTimeZone objects for start/end
    [JsonPropertyName("start")]
    public object? Start { get; set; }

    [JsonPropertyName("end")]
    public object? End { get; set; }

    [JsonPropertyName("isAllDay")]
    public bool? IsAllDay { get; set; }

    [JsonPropertyName("isCancelled")]
    public bool? IsCancelled { get; set; }

    [JsonPropertyName("location")]
    public object? Location { get; set; }

    [JsonPropertyName("organizer")]
    public object? Organizer { get; set; }

    [JsonPropertyName("attendees")]
    public object? Attendees { get; set; }

    [JsonPropertyName("recurrence")]
    public object? Recurrence { get; set; }

    [JsonPropertyName("seriesMasterId")]
    public string? SeriesMasterId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } // singleInstance, occurrence, exception, seriesMaster

    [JsonPropertyName("originalStart")]
    public DateTimeOffset? OriginalStart { get; set; }
}

internal sealed class GraphPlannerTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("bucketId")]
    public string? BucketId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("startDateTime")]
    public DateTimeOffset? StartDateTime { get; set; }

    [JsonPropertyName("dueDateTime")]
    public DateTimeOffset? DueDateTime { get; set; }

    [JsonPropertyName("completedDateTime")]
    public DateTimeOffset? CompletedDateTime { get; set; }

    [JsonPropertyName("percentComplete")]
    public int? PercentComplete { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("hasDescription")]
    public bool? HasDescription { get; set; }

    [JsonPropertyName("assignments")]
    public JsonElement? Assignments { get; set; } // map keyed by userId

    [JsonPropertyName("appliedCategories")]
    public JsonElement? AppliedCategories { get; set; } // map keyed by category
}

internal sealed class GraphChat
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("chatType")]
    public string? ChatType { get; set; } // oneOnOne, group, meeting

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastUpdatedDateTime")]
    public DateTimeOffset? LastUpdatedDateTime { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

internal sealed class GraphChatMember
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("roles")]
    public List<string>? Roles { get; set; }

    [JsonPropertyName("visibleHistoryStartDateTime")]
    public DateTimeOffset? VisibleHistoryStartDateTime { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; } // present on some member types
}

internal sealed class GraphChatMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("deletedDateTime")]
    public DateTimeOffset? DeletedDateTime { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("body")]
    public object? Body { get; set; }

    [JsonPropertyName("from")]
    public object? From { get; set; }

    [JsonPropertyName("attachments")]
    public object? Attachments { get; set; }

    [JsonPropertyName("mentions")]
    public object? Mentions { get; set; }

    [JsonPropertyName("reactions")]
    public object? Reactions { get; set; }

    [JsonPropertyName("replyToId")]
    public string? ReplyToId { get; set; }
}

public sealed class GraphChatHostedContent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("contentBytes")]
    public string? ContentBytes { get; set; }

    [JsonPropertyName("@microsoft.graph.temporaryId")]
    public string? TemporaryId { get; set; }
}

public sealed class GraphGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("mailEnabled")]
    public bool? MailEnabled { get; set; }

    [JsonPropertyName("securityEnabled")]
    public bool? SecurityEnabled { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("groupTypes")]
    public List<string>? GroupTypes { get; set; }

    [JsonPropertyName("resourceProvisioningOptions")]
    public List<string>? ResourceProvisioningOptions { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }
}

public sealed class GraphDirectoryObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }
}

internal sealed class GraphConversation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; set; }

    [JsonPropertyName("lastDeliveredDateTime")]
    public DateTimeOffset? LastDeliveredDateTime { get; set; }

    [JsonPropertyName("uniqueSenders")]
    public List<string>? UniqueSenders { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }
}

internal sealed class GraphConversationThread
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("lastDeliveredDateTime")]
    public DateTimeOffset? LastDeliveredDateTime { get; set; }

    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("uniqueSenders")]
    public List<string>? UniqueSenders { get; set; }
}

internal sealed class GraphPost
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("from")]
    public GraphRecipient? From { get; set; }

    [JsonPropertyName("sender")]
    public GraphRecipient? Sender { get; set; }

    [JsonPropertyName("body")]
    public object? Body { get; set; }

    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; set; }

    [JsonPropertyName("attachments")]
    public object? Attachments { get; set; }
}

public sealed class GraphPlannerPlan
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // Deprecated in Graph: use Container instead
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("createdBy")]
    public GraphIdentitySet? CreatedBy { get; set; }

    [JsonPropertyName("container")]
    public GraphPlannerPlanContainer? Container { get; set; }
}

public sealed class GraphIdentitySet
{
    [JsonPropertyName("user")]
    public GraphIdentity? User { get; set; }

    public sealed class GraphIdentity
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}

public sealed class GraphPlannerPlanContainer
{
    [JsonPropertyName("containerId")]
    public string? ContainerId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } // e.g. "group"

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class GraphPlannerBucket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("orderHint")]
    public string? OrderHint { get; set; }
}

internal sealed class GraphTeamMember
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("roles")]
    public List<string>? Roles { get; set; } // contains "owner" for owners

    [JsonPropertyName("visibleHistoryStartDateTime")]
    public DateTimeOffset? VisibleHistoryStartDateTime { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }
}

public sealed class GraphChannel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("membershipType")]
    public string? MembershipType { get; set; } // standard, private, shared

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("isFavoriteByDefault")]
    public bool? IsFavoriteByDefault { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

public sealed class GraphChannelMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("replyToId")]
    public string? ReplyToId { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("deletedDateTime")]
    public DateTimeOffset? DeletedDateTime { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("from")]
    public GraphChatMessageFromIdentitySet? From { get; set; }

    [JsonPropertyName("body")]
    public object? Body { get; set; }

    [JsonPropertyName("attachments")]
    public object? Attachments { get; set; }

    [JsonPropertyName("mentions")]
    public object? Mentions { get; set; }

    [JsonPropertyName("reactions")]
    public object? Reactions { get; set; }
}

public sealed class GraphChatMessageFromIdentitySet
{
    [JsonPropertyName("user")]
    public GraphChatMessageIdentity? User { get; set; }

    [JsonPropertyName("application")]
    public GraphChatMessageIdentity? Application { get; set; }

    [JsonPropertyName("device")]
    public GraphChatMessageIdentity? Device { get; set; }

    [JsonIgnore]
    public string DisplayName => User?.DisplayName ?? Application?.DisplayName ?? Device?.DisplayName ?? "";
}

public sealed class GraphChatMessageIdentity
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("userIdentityType")]
    public string? UserIdentityType { get; set; }
}

internal sealed class GraphCreatedMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

internal sealed class GraphMoveRequest
{
    [JsonPropertyName("destinationId")]
    public string DestinationId { get; set; } = "";
}

internal sealed class GraphCreateMailFolderRequest
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";
}

internal sealed class GraphCreateCalendarRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class GraphEmailMessageMetadata
{
    // Only fields we want to round-trip for restore
    [JsonPropertyName("isRead")]
    public bool? IsRead { get; set; }

    [JsonPropertyName("importance")]
    public string? Importance { get; set; } // "low" | "normal" | "high"

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("flag")]
    public GraphFollowupFlag? Flag { get; set; }
}

internal sealed class GraphEmailMessagePatch
{
    [JsonPropertyName("isRead")]
    public bool? IsRead { get; set; }

    [JsonPropertyName("importance")]
    public string? Importance { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("flag")]
    public GraphFollowupFlag? Flag { get; set; }
}

internal sealed class GraphFollowupFlag
{
    [JsonPropertyName("flagStatus")]
    public string? FlagStatus { get; set; } // "notFlagged" | "flagged" | "complete"

    // Optional fields if you later include them:
    [JsonPropertyName("startDateTime")]
    public GraphDateTimeTimeZone? StartDateTime { get; set; }

    [JsonPropertyName("dueDateTime")]
    public GraphDateTimeTimeZone? DueDateTime { get; set; }

    [JsonPropertyName("completedDateTime")]
    public GraphDateTimeTimeZone? CompletedDateTime { get; set; }
}

internal sealed class GraphDateTimeTimeZone
{
    [JsonPropertyName("dateTime")]
    public string? DateTime { get; set; } // Graph commonly uses string for this object

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
}

internal sealed class GraphList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("list")]
    public GraphListInfo? List { get; set; }
}

internal sealed class GraphListInfo
{
    [JsonPropertyName("contentTypesEnabled")]
    public bool? ContentTypesEnabled { get; set; }

    [JsonPropertyName("hidden")]
    public bool? Hidden { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }
}

internal sealed class GraphListItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("contentType")]
    public GraphContentTypeInfo? ContentType { get; set; }

    [JsonPropertyName("fields")]
    public JsonElement? Fields { get; set; }
}

internal sealed class GraphContentTypeInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class GraphTeamsTab
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("configuration")]
    public GraphTeamsTabConfiguration? Configuration { get; set; }

    [JsonPropertyName("teamsApp")]
    public GraphTeamsApp? TeamsApp { get; set; }
}

internal sealed class GraphTeamsTabConfiguration
{
    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }

    [JsonPropertyName("removeUrl")]
    public string? RemoveUrl { get; set; }

    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }
}

internal sealed class GraphTeamsAppInstallation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("teamsApp")]
    public GraphTeamsApp? TeamsApp { get; set; }

    [JsonPropertyName("teamsAppDefinition")]
    public GraphTeamsAppDefinition? TeamsAppDefinition { get; set; }
}

internal sealed class GraphTeamsApp
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("distributionMethod")]
    public string? DistributionMethod { get; set; }
}

internal sealed class GraphTeamsAppDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("teamsAppId")]
    public string? TeamsAppId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal sealed class GraphMessageRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("sequence")]
    public int? Sequence { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; set; }

    [JsonPropertyName("hasError")]
    public bool? HasError { get; set; }

    [JsonPropertyName("isReadOnly")]
    public bool? IsReadOnly { get; set; }

    [JsonPropertyName("conditions")]
    public object? Conditions { get; set; }

    [JsonPropertyName("actions")]
    public object? Actions { get; set; }

    [JsonPropertyName("exceptions")]
    public object? Exceptions { get; set; }
}

internal sealed class GraphMailboxSettings
{
    [JsonPropertyName("automaticRepliesSetting")]
    public GraphAutomaticRepliesSetting? AutomaticRepliesSetting { get; set; }

    [JsonPropertyName("archiveFolder")]
    public string? ArchiveFolder { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("language")]
    public GraphLocaleInfo? Language { get; set; }

    [JsonPropertyName("dateFormat")]
    public string? DateFormat { get; set; }

    [JsonPropertyName("timeFormat")]
    public string? TimeFormat { get; set; }

    [JsonPropertyName("workingHours")]
    public object? WorkingHours { get; set; }
}

internal sealed class GraphAutomaticRepliesSetting
{
    [JsonPropertyName("status")]
    public string? Status { get; set; } // disabled, alwaysEnabled, scheduled

    [JsonPropertyName("externalAudience")]
    public string? ExternalAudience { get; set; } // none, contactsOnly, all

    [JsonPropertyName("scheduledStartDateTime")]
    public GraphDateTimeTimeZone? ScheduledStartDateTime { get; set; }

    [JsonPropertyName("scheduledEndDateTime")]
    public GraphDateTimeTimeZone? ScheduledEndDateTime { get; set; }

    [JsonPropertyName("internalReplyMessage")]
    public string? InternalReplyMessage { get; set; }

    [JsonPropertyName("externalReplyMessage")]
    public string? ExternalReplyMessage { get; set; }
}

internal sealed class GraphLocaleInfo
{
    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

internal sealed class GraphPermission
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("roles")]
    public List<string>? Roles { get; set; }

    [JsonPropertyName("grantedTo")]
    public GraphIdentitySet? GrantedTo { get; set; }

    [JsonPropertyName("grantedToIdentities")]
    public List<GraphIdentitySet>? GrantedToIdentities { get; set; }

    [JsonPropertyName("link")]
    public GraphSharingLink? Link { get; set; }

    [JsonPropertyName("invitation")]
    public GraphSharingInvitation? Invitation { get; set; }
}

internal sealed class GraphSharingLink
{
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

internal sealed class GraphSharingInvitation
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("signInRequired")]
    public bool? SignInRequired { get; set; }
}

