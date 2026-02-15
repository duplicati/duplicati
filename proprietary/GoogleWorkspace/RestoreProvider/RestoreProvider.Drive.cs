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
        private string? _cachedDefaultDriveId = null;
        private string? _cachedDefaultFolderId = null;
        private bool _defaultDriveIdChecked = false;

        public async Task<(string? DriveId, string? FolderId)> GetDefaultDriveAndFolder(CancellationToken cancel)
        {
            if (_defaultDriveIdChecked)
                return (_cachedDefaultDriveId, _cachedDefaultFolderId);

            _defaultDriveIdChecked = true;

            var target = Provider.RestoreTarget;
            if (target == null)
                return (null, null);

            string? driveId = null;
            string? userId = null;

            if (target.Type == SourceItemType.DriveFolder)
            {
                driveId = target.Metadata.GetValueOrDefault("gsuite:DriveId");
                _cachedDefaultFolderId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.User)
            {
                userId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserDrive)
            {
                userId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(driveId))
            {
                try
                {
                    var driveService = Provider._apiHelper.GetDriveService(userId);
                    var drive = await driveService.Drives.Get("root").ExecuteAsync(cancel);
                    driveId = drive.Id;
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "GetDefaultDriveIdFailed", ex, $"Failed to get user drive");
                }
            }

            if (driveId != null && string.IsNullOrWhiteSpace(_cachedDefaultFolderId))
            {
                _cachedDefaultDriveId = driveId;

                // Create "Restored" folder
                try
                {
                    var driveService = Provider._apiHelper.GetDriveService(userId ?? "me");

                    // Check if folder already exists
                    var listRequest = driveService.Files.List();
                    listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='Restored' and 'root' in parents and trashed=false";
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
                            MimeType = "application/vnd.google-apps.folder",
                            Parents = new List<string> { "root" }
                        };

                        var createdFolder = await driveService.Files.Create(folderMetadata).ExecuteAsync(cancel);
                        _cachedDefaultFolderId = createdFolder.Id;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "CreateRestoredFolderFailed", ex, $"Failed to create 'Restored' folder in drive {driveId}");
                    // Fallback to root
                    _cachedDefaultFolderId = "root";
                }
            }

            return (_cachedDefaultDriveId, _cachedDefaultFolderId);
        }

        public async Task<string?> CreateFolder(string userId, string parentFolderId, string name, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            // Check if folder already exists
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name.Replace("'", "\\'")}' and '{parentFolderId}' in parents and trashed=false";
            listRequest.Spaces = "drive";
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
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentFolderId }
            };

            var createdFolder = await driveService.Files.Create(folderMetadata).ExecuteAsync(cancel);
            return createdFolder.Id;
        }

        public async Task<string?> UploadFile(string userId, string parentFolderId, string name, Stream contentStream, string? mimeType, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            // Check if file already exists
            if (!Provider._ignoreExisting)
            {
                var listRequest = driveService.Files.List();
                listRequest.Q = $"name='{name.Replace("'", "\\'")}' and '{parentFolderId}' in parents and trashed=false";
                listRequest.Spaces = "drive";
                var existingFiles = await listRequest.ExecuteAsync(cancel);

                if (existingFiles.Files?.Count > 0)
                {
                    var existingFile = existingFiles.Files[0];
                    // Check if it's the same file by size
                    if (existingFile.Size == contentStream.Length)
                    {
                        Log.WriteInformationMessage(LOGTAG, "UploadFileSkipDuplicate", $"File {name} already exists with same size, skipping.");
                        return existingFile.Id;
                    }
                }
            }

            // Upload file
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = new List<string> { parentFolderId }
            };

            // For Google Workspace files, we need to handle them differently
            if (mimeType?.StartsWith("application/vnd.google-apps.") == true)
            {
                // Create the file
                var createdFile = await driveService.Files.Create(fileMetadata).ExecuteAsync(cancel);
                return createdFile.Id;
            }
            else
            {
                // Upload binary content
                var uploadRequest = driveService.Files.Create(fileMetadata, contentStream, mimeType ?? "application/octet-stream");
                var uploadResult = await uploadRequest.UploadAsync(cancel);

                if (uploadResult.Status == UploadStatus.Failed)
                {
                    throw new Exception($"Failed to upload file: {uploadResult.Exception?.Message}");
                }

                return uploadRequest.ResponseBody?.Id;
            }
        }

        public async Task RestorePermissions(string userId, string fileId, Stream permissionsStream, CancellationToken cancel)
        {
            var driveService = Provider._apiHelper.GetDriveService(userId);

            List<Permission>? permissions;
            try
            {
                permissions = await JsonSerializer.DeserializeAsync<List<Permission>>(permissionsStream, cancellationToken: cancel);
            }
            catch
            {
                return;
            }

            if (permissions == null || permissions.Count == 0) return;

            foreach (var permission in permissions)
            {
                try
                {
                    // Skip owner permissions (can't change ownership easily)
                    if (permission.Role == "owner") continue;

                    var newPermission = new Permission
                    {
                        Role = permission.Role,
                        Type = permission.Type,
                        EmailAddress = permission.EmailAddress,
                        Domain = permission.Domain,
                        AllowFileDiscovery = permission.AllowFileDiscovery
                    };

                    await driveService.Permissions.Create(newPermission, fileId).ExecuteAsync(cancel);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestorePermissionFailed", ex, $"Failed to restore permission for file {fileId}");
                }
            }
        }
    }

    private async Task RestoreDriveFolders(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var folders = GetMetadataByType(SourceItemType.DriveFolder);
        if (folders.Count == 0)
            return;

        (var driveId, var defaultFolderId) = await DriveRestore.GetDefaultDriveAndFolder(cancel);

        string? userId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.UserDrive)
        {
            userId = RestoreTarget.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
        }
        else if (RestoreTarget.Type == SourceItemType.DriveFolder)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:UserId");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingUser", null, "Could not determine target user for Drive folder restore.");
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

        (var driveId, var defaultFolderId) = await DriveRestore.GetDefaultDriveAndFolder(cancel);

        string? userId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.UserDrive)
        {
            userId = RestoreTarget.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
        }
        else if (RestoreTarget.Type == SourceItemType.DriveFolder)
        {
            userId = RestoreTarget.Metadata.GetValueOrDefault("gsuite:UserId");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingUser", null, "Could not determine target user for Drive file restore.");
            return;
        }

        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = file.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingContent", null, $"Missing content for file {originalPath}, skipping.");
                    continue;
                }

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

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await DriveRestore.UploadFile(userId, parentId, displayName, contentStream, mimeType, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDriveFilesFailed", ex, $"Failed to restore file {file.Key}");
            }
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
            userId = RestoreTarget.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreDrivePermissionsMissingUser", null, "Could not determine target user for Drive permission restore.");
            return;
        }

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
                    else
                    {
                        // It might be a file - check metadata
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
                    await DriveRestore.RestorePermissions(userId, fileId, contentStream, cancel);
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
