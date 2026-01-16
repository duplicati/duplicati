// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    private EmailRestoreHelper? _emailRestoreHelper = null;
    internal EmailRestoreHelper EmailRestore => _emailRestoreHelper ??= new EmailRestoreHelper(this);
    internal class EmailRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private string? _targetMailboxId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<(string? UserId, string? MailboxId)> GetUserIdAndMailboxTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
            {
                if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetMailboxId))
                    return (null, null);
                return (_targetUserId!, _targetMailboxId!);
            }

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata["o365:Id"]!;
                _targetMailboxId = await GetDefaultRestoreTargetMailbox(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserMailbox)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(_targetUserId))
                    throw new InvalidOperationException("User ID is not set");
                _targetMailboxId = await GetDefaultRestoreTargetMailbox(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserMailboxFolder)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
                _targetMailboxId = target.Metadata["o365:Id"];
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreUserEmailsInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring emails.");
            }

            // Don't try to load again as that could lead to repeated warnings being logged
            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetMailboxId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreUserEmailsMissingIds", null, $"Missing target userId or mailboxId for restoring emails.");
                return (null, null);
            }

            return (_targetUserId, _targetMailboxId);
        }

        private async Task<string> GetDefaultRestoreTargetMailbox(string userId, CancellationToken cancel)
        {
            const string RESTORED_FOLDER_NAME = "Restored";

            var targetNames = new[] { RESTORED_FOLDER_NAME };

            var path = string.Join("/", new[] {
                Uri.EscapeDataString(Office365MetaType.Users.ToString().ToLowerInvariant()),
                Uri.EscapeDataString(userId),
                Uri.EscapeDataString(Office365UserType.Mailbox.ToString().ToLowerInvariant())
            });

            var mailbox = await Provider.SourceProvider.GetEntry(path, true, cancel);
            if (mailbox == null)
                throw new InvalidOperationException($"Mailbox not found for user {userId}");

            await foreach (var folder in mailbox.Enumerate(cancel))
            {
                if (cancel.IsCancellationRequested)
                    break;

                var folderMetadata = await folder.GetMinorMetadata(cancel);
                var folderId = folderMetadata["o365:Id"];
                if (string.IsNullOrWhiteSpace(folderId))
                    continue;

                var folderName = folderMetadata["o365:Name"];
                if (targetNames.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                    return folderId;
            }

            var restoredFolder = await Provider.EmailApi.CreateMailFolderAsync(userId, "msgfolderroot", RESTORED_FOLDER_NAME, cancel);
            return restoredFolder.Id;
        }
    }
}
