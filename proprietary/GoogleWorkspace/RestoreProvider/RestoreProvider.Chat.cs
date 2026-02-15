// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.HangoutsChat.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

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
            var chatService = Provider._apiHelper.GetChatService(userId);

            // Check for duplicates by display name if available
            if (!Provider._ignoreExisting && !string.IsNullOrWhiteSpace(space.DisplayName))
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

            // Clean up properties that shouldn't be sent on creation
            space.Name = null;
            space.CreateTimeDateTimeOffset = null;

            var createdSpace = await chatService.Spaces.Create(space).ExecuteAsync(cancel);
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
    }

    private async Task RestoreChatMessages(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        // Note: Google Chat API has significant limitations for restoring messages
        // Messages can typically only be created by the user who authored them
        // This implementation attempts to restore spaces but may not be able to restore messages

        var spaces = GetMetadataByType(SourceItemType.ChatSpace);
        var messages = GetMetadataByType(SourceItemType.ChatMessage);

        if (spaces.Count == 0 && messages.Count == 0)
            return;

        var userId = await ChatRestore.GetUserIdAndChatTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        // First, restore spaces
        foreach (var space in spaces)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = space.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatSpacesMissingContent", null, $"Missing content for space {originalPath}, skipping.");
                    continue;
                }

                Space? spaceData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    spaceData = await JsonSerializer.DeserializeAsync<Space>(contentStream, cancellationToken: cancel);
                }

                if (spaceData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatSpacesInvalidContent", null, $"Invalid content for space {originalPath}, skipping.");
                    continue;
                }

                await ChatRestore.CreateSpace(userId, spaceData, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChatSpacesFailed", ex, $"Failed to restore Chat space {space.Key}");
            }
        }

        // Log warning about message restoration limitations
        if (messages.Count > 0)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesLimitation", null, $"Found {messages.Count} chat messages to restore. Note: Google Chat API has limitations on message creation - messages can typically only be created by the original author. These messages may not be restorable.");
        }

        // Attempt to restore messages (will likely fail due to API limitations)
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = message.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesMissingContent", null, $"Missing content for message {originalPath}, skipping.");
                    continue;
                }

                // Get space name from parent path
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                var spaceName = message.Value.GetValueOrDefault("gsuite:SpaceName");

                if (string.IsNullOrWhiteSpace(spaceName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesMissingSpace", null, $"Missing space name for message {originalPath}, skipping.");
                    continue;
                }

                Message? messageData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    messageData = await JsonSerializer.DeserializeAsync<Message>(contentStream, cancellationToken: cancel);
                }

                if (messageData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChatMessagesInvalidContent", null, $"Invalid content for message {originalPath}, skipping.");
                    continue;
                }

                await ChatRestore.CreateMessage(userId, spaceName, messageData, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChatMessagesFailed", ex, $"Failed to restore Chat message {message.Key}");
            }
        }
    }
}
