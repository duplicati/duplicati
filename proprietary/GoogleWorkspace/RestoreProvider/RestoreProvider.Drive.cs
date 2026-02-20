// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private DriveRestoreHelper? _driveRestoreHelper = null;
    internal DriveRestoreHelper DriveRestore => _driveRestoreHelper ??= new DriveRestoreHelper(this);

    internal class DriveRestoreHelper(RestoreProvider Provider)
    {
        private string? _cachedDefaultFolderId = null;
        private bool _defaultFolderIdChecked = false;

        public async Task<string?> GetDefaultFolder(CancellationToken cancel)
        {
            if (_defaultFolderIdChecked)
                return _cachedDefaultFolderId;

            _defaultFolderIdChecked = true;

            var target = Provider.RestoreTarget;
            if (target == null)
                return null;


            if (target.Type == SourceItemType.DriveFolder)
                return _cachedDefaultFolderId = target.Metadata.GetValueOrDefault("gsuite:Id");

            string? userId;
            if (target.Type == SourceItemType.User)
            {
                userId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserDrive)
            {
                userId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
            }
            else
            {
                return _cachedDefaultFolderId = null;
            }

            // Create "Restored" folder
            try
            {
                var driveService = Provider._apiHelper.GetDriveService(userId);

                // Check if folder already exists
                var listRequest = driveService.Files.List();
                listRequest.Q = $"mimeType='{GoogleMimeTypes.Folder}' and name='Restored' and 'root' in parents and trashed=false";
                listRequest.Spaces = "drive";
                var existingFolders = await listRequest.ExecuteAsync(cancel);

                if (existingFolders.Files?.Count > 0)
                {
                    _cachedDefaultFolderId = existingFolders.Files[0].Id;
                }
                else
                {
                    // Create new folder
                    var folderMetadata = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = "Restored",
                        MimeType = GoogleMimeTypes.Folder,
                        Parents = new List<string> { "root" }
                    };

                    var createdFolder = await driveService.Files.Create(folderMetadata).ExecuteAsync(cancel);
                    _cachedDefaultFolderId = createdFolder.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "CreateRestoredFolderFailed", ex, $"Failed to create 'Restored' folder for user {userId}");
            }

            return _cachedDefaultFolderId;
        }

        public async Task<string?> CreateFolder(string? userId, string parentFolderId, string name, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            // Check if folder already exists
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='{GoogleMimeTypes.Folder}' and name='{name.Replace("'", "\\'")}' and '{parentFolderId}' in parents and trashed=false";
            listRequest.Spaces = "drive";
            listRequest.SupportsAllDrives = true;
            listRequest.IncludeItemsFromAllDrives = true;
            var existingFolders = await listRequest.ExecuteAsync(cancel);

            if (existingFolders.Files?.Count > 0)
            {
                if (Provider._ignoreExisting)
                {
                    Log.WriteInformationMessage(LOGTAG, "CreateFolderSkipExisting", $"Folder {name} already exists, skipping.");
                    return existingFolders.Files[0].Id;
                }
                // Return existing folder ID
                return existingFolders.Files[0].Id;
            }

            // Create new folder
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                MimeType = GoogleMimeTypes.Folder,
                Parents = new List<string> { parentFolderId }
            };

            var createRequest = driveService.Files.Create(folderMetadata);
            createRequest.SupportsAllDrives = true;
            var createdFolder = await createRequest.ExecuteAsync(cancel);
            return createdFolder.Id;
        }

        public async Task<string?> UploadFile(string? userId, string parentFolderId, string name, Stream contentStream, string? mimeType, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            // Check if file already exists
            if (!Provider._ignoreExisting)
            {
                var listRequest = driveService.Files.List();
                listRequest.Q = $"name='{name.Replace("'", "\\'")}' and '{parentFolderId}' in parents and trashed=false";
                listRequest.Spaces = "drive";
                listRequest.Fields = "nextPageToken, files(id, name, size)";
                listRequest.SupportsAllDrives = true;
                listRequest.IncludeItemsFromAllDrives = true;
                var existingFiles = await listRequest.ExecuteAsync(cancel);
                var existingFile = existingFiles.Files?.FirstOrDefault(x => x.Size == contentStream.Length);

                if (existingFile != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "UploadFileSkipDuplicate", $"File {name} already exists with same size, skipping.");
                    return existingFile.Id;
                }
            }

            // Upload file
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = new List<string> { parentFolderId }
            };

            // For Google Workspace files, we need to handle them differently
            if (mimeType != null && GoogleMimeTypes.IsGoogleDoc(mimeType))
            {
                // Create the file
                var createRequest = driveService.Files.Create(fileMetadata);
                createRequest.SupportsAllDrives = true;
                var createdFile = await createRequest.ExecuteAsync(cancel);
                return createdFile.Id;
            }
            else
            {
                // Upload binary content
                var uploadRequest = driveService.Files.Create(fileMetadata, contentStream, mimeType ?? "application/octet-stream");
                uploadRequest.SupportsAllDrives = true;
                var uploadResult = await uploadRequest.UploadAsync(cancel);

                if (uploadResult.Status == UploadStatus.Failed)
                {
                    throw new Exception($"Failed to upload file: {uploadResult.Exception?.Message}");
                }

                return uploadRequest.ResponseBody?.Id;
            }
        }

        public async Task<string?> UploadFileRevision(string? userId, string fileId, Stream contentStream, string? mimeType, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            // For Google Workspace files, revisions cannot be uploaded directly
            if (mimeType != null && GoogleMimeTypes.IsGoogleDoc(mimeType))
            {
                // Google Workspace files don't support direct revision uploads
                // Revisions are created automatically when the file is edited
                Log.WriteInformationMessage(LOGTAG, "UploadFileRevisionSkipGoogleDoc", "Skipping revision upload for Google Workspace file.");
                return fileId;
            }

            // Upload new revision for binary files
            var uploadRequest = driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId, contentStream, mimeType ?? "application/octet-stream");
            uploadRequest.SupportsAllDrives = true;
            var uploadResult = await uploadRequest.UploadAsync(cancel);

            if (uploadResult.Status == UploadStatus.Failed)
            {
                throw new Exception($"Failed to upload file revision: {uploadResult.Exception?.Message}");
            }

            return fileId;
        }

        public async Task<string?> CreateGoogleWorkspaceFile(string? userId, string parentFolderId, string name, string? mimeType, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                MimeType = mimeType ?? GoogleMimeTypes.Document,
                Parents = new List<string> { parentFolderId }
            };

            var createRequest = driveService.Files.Create(fileMetadata);
            createRequest.SupportsAllDrives = true;
            var createdFile = await createRequest.ExecuteAsync(cancel);
            return createdFile.Id;
        }

        public async Task<string?> ImportGoogleWorkspaceFile(string? userId, string parentFolderId, string name, Stream contentStream, string? targetMimeType, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = new List<string> { parentFolderId }
            };

            // Upload with convert=true to import the file as a Google Workspace file
            var uploadRequest = driveService.Files.Create(fileMetadata, contentStream, targetMimeType ?? "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            uploadRequest.SupportsAllDrives = true;
            var uploadResult = await uploadRequest.UploadAsync(cancel);

            if (uploadResult.Status == UploadStatus.Failed)
            {
                throw new Exception($"Failed to import file: {uploadResult.Exception?.Message}");
            }

            return uploadRequest.ResponseBody?.Id;
        }

        public async Task UpdateFileMetadata(string? userId, string fileId, Stream metadataStream, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            try
            {
                var metadata = await JsonSerializer.DeserializeAsync<Google.Apis.Drive.v3.Data.File>(metadataStream, cancellationToken: cancel);
                if (metadata == null) return;

                // Only update fields that can be modified
                var updateMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = metadata.Name,
                    Description = metadata.Description,
                    ModifiedTimeRaw = metadata.ModifiedTimeRaw,
                    // Note: CreatedTime cannot be changed after file creation
                };

                var updateRequest = driveService.Files.Update(updateMetadata, fileId);
                updateRequest.SupportsAllDrives = true;
                await updateRequest.ExecuteAsync(cancel);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "UpdateFileMetadataFailed", ex, $"Failed to update metadata for file {fileId}");
            }
        }

        public async Task RestoreComments(string? userId, string fileId, Stream commentsStream, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            try
            {
                var comments = await JsonSerializer.DeserializeAsync<Google.Apis.Drive.v3.Data.CommentList>(commentsStream, cancellationToken: cancel);
                if (comments?.Comments == null) return;

                foreach (var comment in comments.Comments)
                {
                    try
                    {
                        var newComment = new Google.Apis.Drive.v3.Data.Comment
                        {
                            Content = comment.Content,
                            QuotedFileContent = comment.QuotedFileContent,
                            Anchor = comment.Anchor
                        };

                        await driveService.Comments.Create(newComment, fileId).ExecuteAsync(cancel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreCommentFailed", ex, $"Failed to restore comment for file {fileId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreCommentsFailed", ex, $"Failed to restore comments for file {fileId}");
            }
        }

        public async Task RestorePermissions(string? userId, string fileId, Stream permissionsStream, bool isSharedDrive, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            PermissionList? permissionList;
            try
            {
                permissionList = await JsonSerializer.DeserializeAsync<PermissionList>(permissionsStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "RestorePermissionsFailed", ex, $"Failed to deserialize permissions for file {fileId}");
                return;
            }

            if (permissionList?.Permissions == null || permissionList.Permissions.Count == 0) return;

            foreach (var permission in permissionList.Permissions)
            {
                try
                {
                    // Skip owner permissions (can't change ownership easily)
                    if (permission.Role == "owner") continue;

                    // Skip permissions with invalid email addresses (e.g., internal Google Chat/Spaces identifiers)
                    if (!string.IsNullOrEmpty(permission.EmailAddress) && !IsValidEmailAddress(permission.EmailAddress))
                        continue;

                    // The "fileOrganizer" role is only valid for shared drives
                    // Skip this role when restoring to a non-shared drive (e.g., user's My Drive)
                    if ((permission.Role == "fileOrganizer" || permission.Role == "organizer") && !isSharedDrive)
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestorePermissionSkipOrganizer", $"Skipping fileOrganizer role for file {fileId} - not a shared drive");
                        continue;
                    }

                    var newPermission = new Permission
                    {
                        Role = permission.Role,
                        Type = permission.Type,
                        EmailAddress = permission.EmailAddress,
                        Domain = permission.Domain,
                        AllowFileDiscovery = permission.AllowFileDiscovery
                    };

                    var createPermissionRequest = driveService.Permissions.Create(newPermission, fileId);
                    createPermissionRequest.SupportsAllDrives = true;
                    await createPermissionRequest.ExecuteAsync(cancel);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestorePermissionFailed", ex, $"Failed to restore permission for file {fileId}");
                }
            }
        }

        private static bool IsValidEmailAddress(string email)
        {
            // Basic email validation - must contain @ and not contain path-like prefixes
            // This filters out internal Google identifiers like "/namespaced-roster/..."
            return email.Contains('@') && !email.StartsWith('/');
        }
    }

    private async Task RestoreDriveFolders(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var folders = GetMetadataByType(SourceItemType.DriveFolder);
        if (folders.Count == 0)
            return;

        var defaultFolderId = await DriveRestore.GetDefaultFolder(cancel);

        string? userId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.UserDrive)
        {
            userId = RestoreTarget.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
        }
        else if (RestoreTarget.Type == SourceItemType.DriveFolder)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:UserId");
        }

        // For shared drive folders, userId may be null - we can still restore using the service account
        // or admin credentials without user impersonation
        if (string.IsNullOrWhiteSpace(userId) && RestoreTarget.Type != SourceItemType.DriveFolder)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingUser", null, $"Could not determine target user for Drive folder restore ({RestoreTarget.Type}).");
            return;
        }

        // Sort folders by path length to ensure parents are created before children
        var sortedFolders = folders.OrderBy(k => k.Key.Split(Path.DirectorySeparatorChar).Length).ToList();

        foreach (var folder in sortedFolders)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = folder.Key;
                var metadata = folder.Value;
                var displayName = metadata.GetValueOrDefault("gsuite:Name") ?? Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar));

                _metadata.TryRemove(originalPath, out _);

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingName", null, $"Missing display name for folder {originalPath}, skipping.");
                    continue;
                }

                // Determine parent folder ID
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                var parentId = defaultFolderId ?? "root";

                if (parentPath != null && _restoredDriveFolderMap.TryGetValue(parentPath, out var mappedParentId))
                {
                    parentId = mappedParentId;
                }

                var newFolderId = await DriveRestore.CreateFolder(userId, parentId, displayName, cancel);
                if (newFolderId != null)
                {
                    _restoredDriveFolderMap[originalPath] = newFolderId;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDriveFoldersFailed", ex, $"Failed to restore folder {folder.Key}");
            }
        }
    }

    private async Task RestoreDriveFiles(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var files = GetMetadataByType(SourceItemType.DriveFile);
        if (files.Count == 0)
            return;

        var defaultFolderId = await DriveRestore.GetDefaultFolder(cancel);

        string? userId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.UserDrive)
        {
            userId = RestoreTarget.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
        }
        else if (RestoreTarget.Type == SourceItemType.DriveFolder)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:UserId");
        }

        // For shared drive folders, userId may be null - we can still restore using the service account
        // or admin credentials without user impersonation
        if (string.IsNullOrWhiteSpace(userId) && RestoreTarget.Type != SourceItemType.DriveFolder)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingUser", null, $"Could not determine target user for Drive file restore ({RestoreTarget.Type}).");
            return;
        }

        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = file.Key;
                var metadata = file.Value;
                var displayName = metadata.GetValueOrDefault("gsuite:Name") ?? Path.GetFileName(originalPath);
                var mimeType = metadata.GetValueOrDefault("gsuite:MimeType");

                // Determine parent folder ID
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                var parentId = defaultFolderId ?? "root";

                if (parentPath != null && _restoredDriveFolderMap.TryGetValue(parentPath, out var mappedParentId))
                {
                    parentId = mappedParentId;
                }

                // Get the content file path (stored at originalPath + "/content")
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);

                string? fileId = null;

                if (contentEntry != null)
                {
                    // For Google Workspace files, the content is an exported format that needs to be imported
                    if (GoogleMimeTypes.IsGoogleDoc(mimeType))
                    {
                        // Get the export MIME type for the Google Workspace file
                        var exportMimeType = GoogleMimeTypes.GetExportMimeType(mimeType!);

                        using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                        {
                            fileId = await DriveRestore.ImportGoogleWorkspaceFile(userId, parentId, displayName, contentStream, exportMimeType, cancel);
                        }
                    }
                    else
                    {
                        // For regular binary files, upload directly
                        using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                        {
                            fileId = await DriveRestore.UploadFile(userId, parentId, displayName, contentStream, mimeType, cancel);
                        }
                    }

                    _metadata.TryRemove(contentPath, out _);
                    _temporaryFiles.TryRemove(contentPath, out var contentFile);
                    contentFile?.Dispose();
                }
                else
                {
                    // No content available, create empty Google Workspace file if applicable
                    if (mimeType != null && GoogleMimeTypes.IsGoogleDoc(mimeType))
                    {
                        fileId = await DriveRestore.CreateGoogleWorkspaceFile(userId, parentId, displayName, mimeType, cancel);
                    }
                    else
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingContent", null, $"Missing content for file {originalPath}, skipping.");
                        continue;
                    }
                }

                if (fileId != null)
                {
                    _restoredDriveFileMap[originalPath] = fileId;

                    // Now restore any revisions for this file
                    await RestoreDriveFileRevisions(userId, fileId, originalPath, mimeType, cancel);

                    // Restore metadata (modified time, description, etc.)
                    await RestoreDriveFileMetadata(userId, fileId, originalPath, cancel);

                    // Restore comments
                    await RestoreDriveFileComments(userId, fileId, originalPath, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDriveFilesFailed", ex, $"Failed to restore file {file.Key}");
            }
        }
    }

    private async Task RestoreDriveFileRevisions(string? userId, string fileId, string originalFilePath, string? mimeType, CancellationToken cancel)
    {
        // Find all revisions for this file
        // Revisions are stored at originalFilePath + "/Revisions/" + revisionId
        var revisionPrefix = SystemIO.IO_OS.PathCombine(originalFilePath, "Revisions");

        var revisionEntries = _temporaryFiles
            .Where(kv => kv.Key.StartsWith(revisionPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (revisionEntries.Count == 0)
            return;

        // Sort revisions by path to maintain chronological order
        var sortedRevisions = revisionEntries.OrderBy(r => r.Key).ToList();

        foreach (var revisionEntry in sortedRevisions)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(revisionEntry.Value))
                {
                    await DriveRestore.UploadFileRevision(userId, fileId, contentStream, mimeType, cancel);
                }

                _temporaryFiles.TryRemove(revisionEntry.Key, out var tempFile);
                tempFile?.Dispose();

                // Also remove from metadata if present
                _metadata.TryRemove(revisionEntry.Key, out _);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreDriveFileRevisionFailed", ex, $"Failed to restore revision {revisionEntry.Key} for file {fileId}");
            }
        }

        // Clean up the Revisions folder metadata entry if it exists
        var revisionsFolderPath = Util.AppendDirSeparator(revisionPrefix);
        _metadata.TryRemove(revisionsFolderPath, out _);
    }

    private async Task RestoreDriveFileMetadata(string? userId, string fileId, string originalFilePath, CancellationToken cancel)
    {
        var metadataPath = SystemIO.IO_OS.PathCombine(originalFilePath, "metadata.json");
        var metadataEntry = _temporaryFiles.GetValueOrDefault(metadataPath);

        if (metadataEntry == null)
            return;

        try
        {
            using (var contentStream = SystemIO.IO_OS.FileOpenRead(metadataEntry))
            {
                await DriveRestore.UpdateFileMetadata(userId, fileId, contentStream, cancel);
            }

            _temporaryFiles.TryRemove(metadataPath, out var tempFile);
            tempFile?.Dispose();

            // Also remove from metadata if present
            _metadata.TryRemove(metadataPath, out _);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFileMetadataFailed", ex, $"Failed to restore metadata for file {fileId}");
        }
    }

    private async Task RestoreDriveFileComments(string? userId, string fileId, string originalFilePath, CancellationToken cancel)
    {
        var commentsPath = SystemIO.IO_OS.PathCombine(originalFilePath, "comments.json");
        var commentsEntry = _temporaryFiles.GetValueOrDefault(commentsPath);

        if (commentsEntry == null)
            return;

        try
        {
            using (var contentStream = SystemIO.IO_OS.FileOpenRead(commentsEntry))
            {
                await DriveRestore.RestoreComments(userId, fileId, contentStream, cancel);
            }

            _temporaryFiles.TryRemove(commentsPath, out var tempFile);
            tempFile?.Dispose();

            // Also remove from metadata if present
            _metadata.TryRemove(commentsPath, out _);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFileCommentsFailed", ex, $"Failed to restore comments for file {fileId}");
        }
    }

    private async Task RestoreDrivePermissions(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var permissions = GetMetadataByType(SourceItemType.DrivePermission);
        if (permissions.Count == 0)
            return;

        string? userId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.UserDrive)
        {
            userId = RestoreTarget.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
        }
        else if (RestoreTarget.Type == SourceItemType.DriveFolder)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:UserId");
        }

        // For shared drive folders, userId may be null - we can still restore using the service account
        // or admin credentials without user impersonation
        if (string.IsNullOrWhiteSpace(userId) && RestoreTarget.Type != SourceItemType.DriveFolder)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDrivePermissionsMissingUser", null, $"Could not determine target user for Drive permission restore ({RestoreTarget.Type}).");
            return;
        }

        // Determine if the restore target is a shared drive
        // The "fileOrganizer" role is only valid for shared drives
        bool isSharedDrive = RestoreTarget.Type == SourceItemType.SharedDrives || RestoreTarget.Type == SourceItemType.DriveFolder;

        foreach (var permission in permissions)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = permission.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDrivePermissionsMissingContent", null, $"Missing content for permission {originalPath}, skipping.");
                    continue;
                }

                // Get the file ID from parent path
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                string? fileId = null;

                if (parentPath != null)
                {
                    // Check if it's a folder
                    if (_restoredDriveFolderMap.TryGetValue(parentPath, out var folderId))
                    {
                        fileId = folderId;
                    }
                    // Check if it's a file (using the restored file map)
                    else if (_restoredDriveFileMap.TryGetValue(parentPath, out var restoredFileId))
                    {
                        fileId = restoredFileId;
                    }
                    else
                    {
                        // It might be a file - check metadata (fallback for older backups)
                        var fileMetadata = _metadata.FirstOrDefault(m => m.Key == parentPath.TrimEnd(Path.DirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(fileMetadata.Key))
                        {
                            fileId = fileMetadata.Value.GetValueOrDefault("gsuite:Id");
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(fileId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDrivePermissionsMissingFileId", null, $"Could not find file ID for permission {originalPath}, skipping.");
                    continue;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await DriveRestore.RestorePermissions(userId, fileId, contentStream, isSharedDrive, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDrivePermissionsFailed", ex, $"Failed to restore permission {permission.Key}");
            }
        }
    }
}
