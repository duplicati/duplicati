// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Gmail.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private GmailRestoreHelper? _gmailRestoreHelper = null;
    internal GmailRestoreHelper GmailRestore => _gmailRestoreHelper ??= new GmailRestoreHelper(this);

    internal class GmailRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private string? _targetLabelId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<(string? UserId, string? LabelId)> GetUserIdAndLabelTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
            {
                if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetLabelId))
                    return (null, null);
                return (_targetUserId!, _targetLabelId!);
            }

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
                if (!string.IsNullOrWhiteSpace(_targetUserId))
                    _targetLabelId = await GetDefaultRestoreTargetLabel(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserGmail)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(_targetUserId))
                    throw new InvalidOperationException("User ID is not set");
                _targetLabelId = await GetDefaultRestoreTargetLabel(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.GmailLabel)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
                _targetLabelId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreGmailInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring Gmail items.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetLabelId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreGmailMissingIds", null, $"Missing target userId or labelId for restoring Gmail items.");
                return (null, null);
            }

            return (_targetUserId, _targetLabelId);
        }

        private async Task<string> GetDefaultRestoreTargetLabel(string userId, CancellationToken cancel)
        {
            const string RESTORED_LABEL_NAME = "Restored";

            var gmailService = Provider._apiHelper.GetGmailService(userId);

            // Check if label already exists
            var labelsResponse = await gmailService.Users.Labels.List(userId).ExecuteAsync(cancel);
            var existingLabel = labelsResponse.Labels?.FirstOrDefault(l =>
                l.Name?.Equals(RESTORED_LABEL_NAME, StringComparison.OrdinalIgnoreCase) == true);

            if (existingLabel != null)
                return existingLabel.Id;

            // Create new label
            var newLabel = new Label
            {
                Name = RESTORED_LABEL_NAME,
                LabelListVisibility = "labelShow",
                MessageListVisibility = "show"
            };

            var createdLabel = await gmailService.Users.Labels.Create(newLabel, userId).ExecuteAsync(cancel);
            return createdLabel.Id;
        }

        public async Task<string?> RestoreLabelFromMetadata(string userId, Dictionary<string, string?> metadata, CancellationToken cancel)
        {
            var labelName = metadata.GetValueOrDefault("gsuite:Name");
            if (string.IsNullOrWhiteSpace(labelName))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreLabelMissingName", null, "Label name is missing in metadata, skipping.");
                return null;
            }

            var gmailService = Provider._apiHelper.GetGmailService(userId);

            // Check if label already exists
            var labelsResponse = await gmailService.Users.Labels.List(userId).ExecuteAsync(cancel);
            var existingLabel = labelsResponse.Labels?.FirstOrDefault(l =>
                l.Name?.Equals(labelName, StringComparison.OrdinalIgnoreCase) == true);

            if (existingLabel != null && Provider._ignoreExisting)
            {
                Log.WriteInformationMessage(LOGTAG, "RestoreLabelSkipExisting", $"Label {labelName} already exists, skipping.");
                return existingLabel.Id;
            }

            // Build label from metadata
            var label = new Label
            {
                Name = labelName,
                LabelListVisibility = metadata.GetValueOrDefault("gsuite:LabelListVisibility") ?? "labelShow",
                MessageListVisibility = metadata.GetValueOrDefault("gsuite:MessageListVisibility") ?? "show"
            };

            // Add color if present in metadata
            var colorBackground = metadata.GetValueOrDefault("gsuite:ColorBackground");
            var colorText = metadata.GetValueOrDefault("gsuite:ColorText");
            if (!string.IsNullOrEmpty(colorBackground) || !string.IsNullOrEmpty(colorText))
            {
                label.Color = new LabelColor
                {
                    BackgroundColor = colorBackground,
                    TextColor = colorText
                };
            }

            if (existingLabel != null)
            {
                // Update existing label
                var updatedLabel = await gmailService.Users.Labels.Update(label, userId, existingLabel.Id).ExecuteAsync(cancel);
                Log.WriteInformationMessage(LOGTAG, "RestoreLabelUpdated", $"Updated existing label: {labelName}");
                return updatedLabel.Id;
            }
            else
            {
                // Create new label
                var createdLabel = await gmailService.Users.Labels.Create(label, userId).ExecuteAsync(cancel);
                Log.WriteInformationMessage(LOGTAG, "RestoreLabelCreated", $"Created new label: {labelName}");
                return createdLabel.Id;
            }
        }

        public async Task RestoreMessage(string userId, string labelId, Stream messageStream, string? messageId, CancellationToken cancel)
        {
            var gmailService = Provider._apiHelper.GetGmailService(userId);

            // Check for duplicates by Message-Id if available
            if (!Provider._ignoreExisting && !string.IsNullOrWhiteSpace(messageId))
            {
                // Search for existing message
                var searchResponse = await gmailService.Users.Messages.List(userId).ExecuteAsync(cancel);
                if (searchResponse.Messages?.Any() == true)
                {
                    foreach (var existingMsg in searchResponse.Messages)
                    {
                        var fullMsg = await gmailService.Users.Messages.Get(userId, existingMsg.Id).ExecuteAsync(cancel);
                        var headers = fullMsg.Payload?.Headers;
                        var existingMessageId = headers?.FirstOrDefault(h => h.Name == "Message-Id")?.Value;

                        if (existingMessageId == messageId)
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreMessageSkipDuplicate", $"Message with Message-Id {messageId} already exists, skipping.");
                            return;
                        }
                    }
                }
            }

            // Read the EML content and base64url encode it
            using var memoryStream = new MemoryStream();
            await messageStream.CopyToAsync(memoryStream, cancel);
            var rawBytes = memoryStream.ToArray();
            var base64UrlRaw = ToBase64Url(rawBytes);

            // Import the message
            var importRequest = new Message
            {
                Raw = base64UrlRaw,
                LabelIds = new List<string> { labelId }
            };

            await gmailService.Users.Messages.Import(importRequest, userId).ExecuteAsync(cancel);
        }

        private static string ToBase64Url(byte[] bytes)
        {
            var base64 = Convert.ToBase64String(bytes);
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public async Task RestoreSettings(string userId, Stream settingsStream, CancellationToken cancel)
        {
            var settings = await JsonSerializer.DeserializeAsync<ImapSettings>(settingsStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            if (settings == null) return;

            var gmailService = Provider._apiHelper.GetGmailService(userId);
            await gmailService.Users.Settings.UpdateImap(settings, userId).ExecuteAsync(cancel);
        }

        public async Task RestoreFilter(string userId, Stream filterStream, CancellationToken cancel)
        {
            var filter = await JsonSerializer.DeserializeAsync<Filter>(filterStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            if (filter == null) return;

            var gmailService = Provider._apiHelper.GetGmailService(userId);
            await gmailService.Users.Settings.Filters.Create(filter, userId).ExecuteAsync(cancel);
        }

        public async Task RestoreForwarding(string userId, Stream forwardingStream, CancellationToken cancel)
        {
            var forwarding = await JsonSerializer.DeserializeAsync<AutoForwarding>(forwardingStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            if (forwarding == null) return;

            var gmailService = Provider._apiHelper.GetGmailService(userId);
            await gmailService.Users.Settings.UpdateAutoForwarding(forwarding, userId).ExecuteAsync(cancel);
        }

        public async Task RestoreVacation(string userId, Stream vacationStream, CancellationToken cancel)
        {
            var vacation = await JsonSerializer.DeserializeAsync<VacationSettings>(vacationStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            if (vacation == null) return;

            var gmailService = Provider._apiHelper.GetGmailService(userId);
            await gmailService.Users.Settings.UpdateVacation(vacation, userId).ExecuteAsync(cancel);
        }

        public async Task RestoreSignature(string userId, string sendAsEmail, Stream signatureStream, CancellationToken cancel)
        {
            var sendAs = await JsonSerializer.DeserializeAsync<SendAs>(signatureStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            if (sendAs == null) return;

            var gmailService = Provider._apiHelper.GetGmailService(userId);
            await gmailService.Users.Settings.SendAs.Update(sendAs, userId, sendAsEmail).ExecuteAsync(cancel);
        }
    }

    private async Task RestoreGmailLabels(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var labels = GetMetadataByType(SourceItemType.GmailLabel);
        if (labels.Count == 0)
            return;

        (var userId, var defaultLabelId) = await GmailRestore.GetUserIdAndLabelTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var label in labels)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = label.Key;
                var metadata = label.Value;

                // Restore label using metadata (no content entry needed for labels)
                var newLabelId = await GmailRestore.RestoreLabelFromMetadata(userId, metadata, cancel);
                if (newLabelId != null)
                {
                    _restoredLabelMap[originalPath] = newLabelId;
                }

                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGmailLabelsFailed", ex, $"Failed to restore label {label.Key}");
            }
        }
    }

    private async Task RestoreGmailMessages(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var messages = GetMetadataByType(SourceItemType.GmailMessage);
        if (messages.Count == 0)
            return;

        (var userId, var defaultLabelId) = await GmailRestore.GetUserIdAndLabelTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(defaultLabelId))
            return;

        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = message.Key;
                var metadata = message.Value;

                // Find the .eml file in the message folder
                _temporaryFiles.TryGetValue(originalPath, out var emlTempFile);
                if (emlTempFile == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGmailMessagesMissingContent", null, $"Missing .eml file for message {originalPath}, skipping.");
                    continue;
                }

                // Get Message-Id from metadata for duplicate detection
                var messageId = metadata.GetValueOrDefault("gsuite:Message-Id");

                // Determine target label
                var targetLabelId = defaultLabelId;
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                if (parentPath != null && _restoredLabelMap.TryGetValue(parentPath, out var mappedLabelId))
                {
                    targetLabelId = mappedLabelId;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(emlTempFile))
                {
                    await GmailRestore.RestoreMessage(userId, targetLabelId, contentStream, messageId, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var emlFile);
                emlFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGmailMessagesFailed", ex, $"Failed to restore message {message.Key}");
            }
        }
    }

    private async Task RestoreGmailSettings(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var settings = GetMetadataByType(SourceItemType.GmailSettings);
        if (settings.Count == 0)
            return;

        (var userId, _) = await GmailRestore.GetUserIdAndLabelTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var setting in settings)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = setting.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGmailSettingsMissingContent", null, $"Missing content for setting {originalPath}, skipping.");
                    continue;
                }

                var settingType = setting.Value.GetValueOrDefault("gsuite:SettingType");

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    switch (settingType)
                    {
                        case "ImapSettings":
                            await GmailRestore.RestoreSettings(userId, contentStream, cancel);
                            break;
                        case "Filter":
                            await GmailRestore.RestoreFilter(userId, contentStream, cancel);
                            break;
                        case "AutoForwarding":
                            await GmailRestore.RestoreForwarding(userId, contentStream, cancel);
                            break;
                        case "VacationSettings":
                            await GmailRestore.RestoreVacation(userId, contentStream, cancel);
                            break;
                        case "SendAs":
                            var sendAsEmail = setting.Value.GetValueOrDefault("gsuite:SendAsEmail") ?? userId;
                            await GmailRestore.RestoreSignature(userId, sendAsEmail, contentStream, cancel);
                            break;
                        default:
                            Log.WriteWarningMessage(LOGTAG, "RestoreGmailSettingsUnknownType", null, $"Unknown setting type {settingType} for {originalPath}, skipping.");
                            break;
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGmailSettingsFailed", ex, $"Failed to restore setting {setting.Key}");
            }
        }
    }
}
