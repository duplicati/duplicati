// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.Upload;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Helper class for JSON serialization that respects Newtonsoft.Json ignore attributes
/// when using System.Text.Json.
/// </summary>
internal static class GoogleChatJsonHelper
{
    private static readonly JsonSerializerOptions _options;

    static GoogleChatJsonHelper()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // Use exact C# member names (PascalCase)
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { RespectNewtonsoftJsonIgnore }
            }
        };
    }

    /// <summary>
    /// Gets the JSON serializer options that respect Newtonsoft.Json ignore attributes.
    /// </summary>
    public static JsonSerializerOptions Options => _options;

    /// <summary>
    /// Modifier that ignores properties marked with Newtonsoft.Json.JsonIgnoreAttribute.
    /// </summary>
    private static void RespectNewtonsoftJsonIgnore(JsonTypeInfo typeInfo)
    {
        // Collect properties to remove
        var propertiesToRemove = new List<JsonPropertyInfo>();

        foreach (var property in typeInfo.Properties)
        {
            // Check if the property has Newtonsoft.Json.JsonIgnoreAttribute
            var propertyInfo = property.AttributeProvider as System.Reflection.PropertyInfo;
            if (propertyInfo != null)
            {
                var hasJsonIgnore = propertyInfo.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), inherit: true).Any();
                if (hasJsonIgnore)
                {
                    propertiesToRemove.Add(property);
                }
            }
        }

        // Remove the ignored properties from the type info
        foreach (var property in propertiesToRemove)
        {
            typeInfo.Properties.Remove(property);
        }
    }
}

partial class RestoreProvider
{
    private ChatRestoreHelper? _chatRestoreHelper = null;
    internal ChatRestoreHelper ChatRestore => _chatRestoreHelper ??= new ChatRestoreHelper(this);

    internal class ChatRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<string?> GetUserIdAndChatTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
                return _targetUserId;

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            // Only restore Chat messages if the target is a User or UserChat
            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserChat)
            {
                _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
            }
            else if (target.Type == SourceItemType.ChatSpace)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:UserId");
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring chat messages. Chat messages can only be restored to a User, UserChat, or ChatSpace target.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatMissingUserId", null, $"Missing target userId for restoring chat messages.");
                return null;
            }

            return _targetUserId;
        }

        public async Task<string?> CreateSpace(string userId, Space space, CancellationToken cancel)
        {
            var chatService = Provider._apiHelper.GetChatCreateSpaceService(userId);

            if (string.IsNullOrWhiteSpace(space.DisplayName))
                space.DisplayName = "Restored Space";

            // Check for duplicates by display name if available
            if (!Provider._ignoreExisting)
            {
                try
                {
                    var existingSpaces = await chatService.Spaces.List().ExecuteAsync(cancel);
                    var duplicate = existingSpaces.Spaces?.FirstOrDefault(s =>
                        s.DisplayName?.Equals(space.DisplayName, StringComparison.OrdinalIgnoreCase) == true);

                    if (duplicate != null)
                    {
                        Log.WriteInformationMessage(LOGTAG, "CreateSpaceSkipDuplicate", $"Space with name '{space.DisplayName}' already exists, skipping.");
                        return duplicate.Name;
                    }
                }
                catch
                {
                    // List might fail, continue with creation
                }
            }

            // Store the original space history state to set after creation
            var originalHistoryState = space.SpaceHistoryState;

            // Clean up properties that shouldn't be sent on creation
            space.Name = null;
#pragma warning disable CS0618 // Type or member is obsolete
            space.CreateTime = null;
#pragma warning restore CS0618 // Type or member is obsolete
            space.CreateTimeDateTimeOffset = null;
            space.SpaceHistoryState = null;
            space.Type = null;
            space.SpaceType = "SPACE";
            space.SingleUserBotDm = null;

            var createdSpace = await chatService.Spaces.Create(space).ExecuteAsync(cancel);

            // If there was a history state, update the space to set it
            if (!string.IsNullOrEmpty(originalHistoryState) && !string.IsNullOrEmpty(createdSpace.Name))
            {
                try
                {
                    var updateSpace = new Space
                    {
                        SpaceHistoryState = originalHistoryState
                    };
                    var updateRequest = chatService.Spaces.Patch(updateSpace, createdSpace.Name);
                    updateRequest.UpdateMask = "spaceHistoryState";
                    await updateRequest.ExecuteAsync(cancel);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "UpdateSpaceHistoryStateFailed", ex, $"Failed to update space history state for {createdSpace.Name}");
                }
            }

            return createdSpace.Name;
        }

        public async Task<string?> CreateMessage(string userId, string spaceName, Message message, CancellationToken cancel)
        {
            var chatService = Provider._apiHelper.GetChatService(userId);

            // Clean up properties that shouldn't be sent on creation
            message.Name = null;
#pragma warning disable CS0618 // Type or member is obsolete
            message.CreateTime = null;
            message.LastUpdateTime = null;
#pragma warning restore CS0618 // Type or member is obsolete
            message.CreateTimeDateTimeOffset = null;
            message.LastUpdateTimeDateTimeOffset = null;
            message.AccessoryWidgets = null;

            // Note: Creating messages in Chat API has limitations
            // Messages can typically only be created by the user who is sending them
            // For restore purposes, we attempt to create the message
            try
            {
                var createdMessage = await chatService.Spaces.Messages.Create(message, spaceName).ExecuteAsync(cancel);
                return createdMessage.Name;
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "CreateMessageFailed", ex, $"Failed to create message in space {spaceName}. Note: Chat API has limitations on message creation.");
                return null;
            }
        }

        public async Task<string?> CreateMessageWithAttachment(string userId, string spaceName, Message message, string attachmentFileName, Stream attachmentStream, string? contentType, CancellationToken cancel)
        {
            var chatService = Provider._apiHelper.GetChatService(userId);

            // Clean up properties that shouldn't be sent on creation
            message.Name = null;
#pragma warning disable CS0618 // Type or member is obsolete
            message.CreateTime = null;
            message.LastUpdateTime = null;
#pragma warning restore CS0618 // Type or member is obsolete
            message.CreateTimeDateTimeOffset = null;
            message.LastUpdateTimeDateTimeOffset = null;

            try
            {
                // For Chat API, attachments need to be handled differently
                // We upload the attachment to Drive first, then reference it in the message
                // This is a workaround since the Chat API doesn't support direct binary uploads
                // in the same way as other Google APIs
                var driveService = Provider._apiHelper.GetDriveService(userId);

                // Upload attachment to Drive
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = attachmentFileName
                };

                var uploadRequest = driveService.Files.Create(fileMetadata, attachmentStream, contentType ?? "application/octet-stream");
                var uploadResult = await uploadRequest.UploadAsync(cancel);

                if (uploadResult.Status == UploadStatus.Failed)
                {
                    Log.WriteWarningMessage(LOGTAG, "CreateMessageAttachmentUploadFailed", uploadResult.Exception, $"Failed to upload attachment to Drive for message in space {spaceName}.");
                    // Fall back to creating message without attachment
                    return await CreateMessage(userId, spaceName, message, cancel);
                }

                var fileId = uploadRequest.ResponseBody?.Id;
                if (string.IsNullOrEmpty(fileId))
                {
                    Log.WriteWarningMessage(LOGTAG, "CreateMessageAttachmentNoFileId", null, $"No file ID returned after uploading attachment for message in space {spaceName}.");
                    return await CreateMessage(userId, spaceName, message, cancel);
                }

                // Set up attachment in the message
                message.Attachment = new List<Attachment>
                {
                    new Attachment
                    {
                        DriveDataRef = new DriveDataRef
                        {
                            DriveFileId = fileId
                        },
                        ContentName = attachmentFileName,
                        ContentType = contentType
                    }
                };

                // Create the message with the Drive attachment reference
                var createdMessage = await chatService.Spaces.Messages.Create(message, spaceName).ExecuteAsync(cancel);
                return createdMessage.Name;
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "CreateMessageWithAttachmentFailed", ex, $"Failed to create message with attachment in space {spaceName}. Note: Chat API has limitations on message creation.");
                return null;
            }
        }
    }

    private async Task RestoreChatMessages(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        // Note: Google Chat API has significant limitations for restoring messages
        // Messages will appear as being created by the impersonated user, not the original sender
        // To fix this, the Import API would need to be used

        var spaces = GetMetadataByType(SourceItemType.ChatSpace);
        var messages = GetMetadataByType(SourceItemType.ChatMessage);

        if (spaces.Count == 0 && messages.Count == 0)
            return;

        var userId = await ChatRestore.GetUserIdAndChatTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        // First, restore spaces and build a map of original space paths to created space names
        var spacePathToNameMap = new Dictionary<string, string>();
        foreach (var space in spaces)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = space.Key;
                var contentJsonPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentJsonPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatSpacesMissingContent", null, $"Missing content for space {originalPath}, skipping.");
                    continue;
                }

                Space? spaceData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    spaceData = await JsonSerializer.DeserializeAsync<Space>(contentStream, GoogleChatJsonHelper.Options, cancellationToken: cancel);
                }

                if (spaceData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatSpacesInvalidContent", null, $"Invalid content for space {originalPath}, skipping.");
                    continue;
                }

                var createdSpaceName = await ChatRestore.CreateSpace(userId, spaceData, cancel);
                if (!string.IsNullOrEmpty(createdSpaceName))
                {
                    spacePathToNameMap[originalPath] = createdSpaceName;
                }

                _metadata.TryRemove(originalPath, out _);
                _metadata.TryRemove(contentJsonPath, out _);
                _temporaryFiles.TryRemove(contentJsonPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChatSpacesFailed", ex, $"Failed to restore Chat space {space.Key}");
            }
        }

        // Group attachments by message path
        var attachments = GetMetadataByType(SourceItemType.ChatAttachment)
            .GroupBy(k => Util.AppendDirSeparator(Path.GetDirectoryName(k.Key.TrimEnd(Path.DirectorySeparatorChar)) ?? ""))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Attempt to restore messages (will likely fail due to API limitations)
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = message.Key;
                var messageJsonPath = SystemIO.IO_OS.PathCombine(originalPath, "message.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(messageJsonPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesMissingContent", null, $"Missing content for message {originalPath}, skipping.");
                    continue;
                }

                // Get space name from the restored spaces map
                // The message's parent path corresponds to the space path
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                if (!spacePathToNameMap.TryGetValue(parentPath, out var spaceName) || string.IsNullOrWhiteSpace(spaceName))
                {
                    // Fallback to metadata if available
                    spaceName = message.Value.GetValueOrDefault("gsuite:SpaceName");
                }

                if (string.IsNullOrWhiteSpace(spaceName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesMissingSpace", null, $"Missing space name for message {originalPath}, skipping.");
                    continue;
                }

                Message? messageData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    messageData = await JsonSerializer.DeserializeAsync<Message>(contentStream, GoogleChatJsonHelper.Options, cancellationToken: cancel);
                }

                if (messageData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesInvalidContent", null, $"Invalid content for message {originalPath}, skipping.");
                    continue;
                }

                // Check for attachments
                var messagePathWithSep = Util.AppendDirSeparator(originalPath);
                if (attachments.TryGetValue(messagePathWithSep, out var messageAttachments) && messageAttachments.Count > 0)
                {
                    // For now, we only support one attachment per message via media upload
                    // Additional attachments would need to be handled separately
                    var att = messageAttachments.First();
                    var attPath = att.Key;
                    var attMetadata = att.Value;
                    var attFileName = attMetadata.GetValueOrDefault("gsuite:Name") ?? "attachment";
                    var attContentType = attMetadata.GetValueOrDefault("gsuite:ContentType") ?? "application/octet-stream";

                    if (_temporaryFiles.TryRemove(attPath, out var attContent))
                    {
                        try
                        {
                            using var attStream = SystemIO.IO_OS.FileOpenRead(attContent);
                            await ChatRestore.CreateMessageWithAttachment(userId, spaceName, messageData, attFileName, attStream, attContentType, cancel);

                            // Clean up all attachments for this message
                            foreach (var attachment in messageAttachments)
                            {
                                _metadata.TryRemove(attachment.Key, out _);
                                if (_temporaryFiles.TryRemove(attachment.Key, out var attFile))
                                {
                                    attFile?.Dispose();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "RestoreChatMessageAttachmentFailed", ex, $"Failed to restore attachment for message {originalPath}");
                            // Fall back to creating message without attachment
                            await ChatRestore.CreateMessage(userId, spaceName, messageData, cancel);
                        }
                    }
                    else
                    {
                        await ChatRestore.CreateMessage(userId, spaceName, messageData, cancel);
                    }
                }
                else
                {
                    await ChatRestore.CreateMessage(userId, spaceName, messageData, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _metadata.TryRemove(messageJsonPath, out _);
                _temporaryFiles.TryRemove(messageJsonPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChatMessagesFailed", ex, $"Failed to restore Chat message {message.Key}");
            }
        }
    }
}
