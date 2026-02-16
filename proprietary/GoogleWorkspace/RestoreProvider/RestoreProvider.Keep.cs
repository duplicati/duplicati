// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Keep.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private KeepRestoreHelper? _keepRestoreHelper = null;
    internal KeepRestoreHelper KeepRestore => _keepRestoreHelper ??= new KeepRestoreHelper(this);

    internal class KeepRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<string?> GetUserIdAndKeepTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
                return _targetUserId;

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            // Only restore Keep notes if the target is a User or UserKeep
            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserKeep)
            {
                _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreKeepInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring Keep notes. Keep notes can only be restored to a User or UserKeep target.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreKeepMissingUserId", null, $"Missing target userId for restoring Keep notes.");
                return null;
            }

            return _targetUserId;
        }

        public async Task<string?> CreateNote(string userId, Note note, CancellationToken cancel)
        {
            var keepService = Provider._apiHelper.GetKeepService(userId);

            // Check for duplicates by title if available
            if (!Provider._ignoreExisting && !string.IsNullOrWhiteSpace(note.Title))
            {
                try
                {
                    var existingNotes = await keepService.Notes.List().ExecuteAsync(cancel);
                    var duplicate = existingNotes.Notes?.FirstOrDefault(n =>
                        n.Title?.Equals(note.Title, StringComparison.OrdinalIgnoreCase) == true);

                    if (duplicate != null)
                    {
                        Log.WriteInformationMessage(LOGTAG, "CreateNoteSkipDuplicate", $"Note with title '{note.Title}' already exists, skipping.");
                        return duplicate.Name;
                    }
                }
                catch
                {
                    // List might fail, continue with creation
                }
            }

            // Clean up properties that shouldn't be sent on creation
            note.Name = null;
            note.CreateTimeDateTimeOffset = null;
            note.UpdateTimeDateTimeOffset = null;

            var createdNote = await keepService.Notes.Create(note).ExecuteAsync(cancel);
            return createdNote.Name;
        }
    }

    private async Task RestoreKeepNotes(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var notes = GetMetadataByType(SourceItemType.KeepNote);
        if (notes.Count == 0)
            return;

        var userId = await KeepRestore.GetUserIdAndKeepTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var note in notes)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = note.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreKeepNotesMissingContent", null, $"Missing content for note {originalPath}, skipping.");
                    continue;
                }

                Note? noteData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    noteData = await JsonSerializer.DeserializeAsync<Note>(contentStream, cancellationToken: cancel);
                }

                if (noteData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreKeepNotesInvalidContent", null, $"Invalid content for note {originalPath}, skipping.");
                    continue;
                }

                await KeepRestore.CreateNote(userId, noteData, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreKeepNotesFailed", ex, $"Failed to restore Keep note {note.Key}");
            }
        }
    }
}
