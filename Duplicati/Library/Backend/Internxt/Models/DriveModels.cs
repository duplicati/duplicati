// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
using System.Text.Json.Serialization;

namespace Duplicati.Library.Backend.Internxt.Models;

/// <summary>
/// Response from POST /drive/auth/login
/// </summary>
public sealed class LoginSecurityResponse
{
    /// <summary>
    /// The encrypted salt for the password hash
    /// </summary>
    [JsonPropertyName("sKey")]
    public string? SKey { get; set; }

    /// <summary>
    /// Whether two-factor authentication is enabled
    /// </summary>
    [JsonPropertyName("tfa")]
    public bool? Tfa { get; set; }
}

/// <summary>
/// Request body for POST /drive/auth/cli/login/access
/// </summary>
public sealed class LoginAccessRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("tfa")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tfa { get; set; }
}

/// <summary>
/// Response from POST /drive/auth/cli/login/access and GET /drive/users/cli/refresh
/// </summary>
public sealed class LoginAccessResponse
{
    [JsonPropertyName("user")]
    public UserDto? User { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("newToken")]
    public string? NewToken { get; set; }
}

/// <summary>
/// The user object in the login response
/// </summary>
public sealed class UserDto
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>
    /// The mnemonic, encrypted with the plain-text password
    /// </summary>
    [JsonPropertyName("mnemonic")]
    public string? Mnemonic { get; set; }

    /// <summary>
    /// The UUID of the root folder
    /// </summary>
    [JsonPropertyName("rootFolderId")]
    public string? RootFolderId { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>
    /// The network bucket holding the user's files
    /// </summary>
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    /// <summary>
    /// The user name for the network API
    /// </summary>
    [JsonPropertyName("bridgeUser")]
    public string? BridgeUser { get; set; }
}

/// <summary>
/// A file entry in the Drive API
/// </summary>
public sealed class FileDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>
    /// The network file id, null for empty files
    /// </summary>
    [JsonPropertyName("fileId")]
    public string? FileId { get; set; }

    [JsonPropertyName("plainName")]
    public string? PlainName { get; set; }

    /// <summary>
    /// The legacy encrypted name, used only when plainName is missing
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The extension without the dot, or empty
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The size; the server sends this as a string
    /// </summary>
    [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Size { get; set; }

    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("folderUuid")]
    public string? FolderUuid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("modificationTime")]
    public DateTime? ModificationTime { get; set; }

    [JsonPropertyName("creationTime")]
    public DateTime? CreationTime { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Some server versions answer existence checks with this flag
    /// </summary>
    [JsonPropertyName("exists")]
    public bool? Exists { get; set; }

    /// <summary>
    /// Joins the plain name and type back into a file name
    /// </summary>
    public string FullName => InternxtClient.JoinName(PlainName ?? Name ?? string.Empty, Type);
}

/// <summary>
/// A folder entry in the Drive API
/// </summary>
public sealed class FolderDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("plainName")]
    public string? PlainName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("parentUuid")]
    public string? ParentUuid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("modificationTime")]
    public DateTime? ModificationTime { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Response from GET /drive/folders/v2/content/{uuid}/files
/// </summary>
public sealed class FilesPageResponse
{
    [JsonPropertyName("files")]
    public List<FileDto> Files { get; set; } = [];

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Response from GET /drive/folders/v2/content/{uuid}/folders
/// </summary>
public sealed class FoldersPageResponse
{
    [JsonPropertyName("folders")]
    public List<FolderDto> Folders { get; set; } = [];

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Request body for POST /drive/folders
/// </summary>
public sealed class CreateFolderRequest
{
    [JsonPropertyName("plainName")]
    public string PlainName { get; set; } = string.Empty;

    [JsonPropertyName("parentFolderUuid")]
    public string ParentFolderUuid { get; set; } = string.Empty;
}

/// <summary>
/// Request body for POST /drive/files
/// </summary>
public sealed class CreateFileRequest
{
    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = string.Empty;

    [JsonPropertyName("fileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileId { get; set; }

    [JsonPropertyName("encryptVersion")]
    public string EncryptVersion { get; set; } = InternxtCrypto.EncryptVersion;

    [JsonPropertyName("folderUuid")]
    public string FolderUuid { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("plainName")]
    public string PlainName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("modificationTime")]
    public DateTime ModificationTime { get; set; }

    [JsonPropertyName("creationTime")]
    public DateTime CreationTime { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}

/// <summary>
/// Request body for PUT /drive/files/{uuid}
/// </summary>
public sealed class ReplaceFileRequest
{
    [JsonPropertyName("fileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileId { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("modificationTime")]
    public DateTime ModificationTime { get; set; }
}

/// <summary>
/// Request body for PUT /drive/files/{uuid}/meta
/// </summary>
public sealed class UpdateFileMetaRequest
{
    [JsonPropertyName("plainName")]
    public string PlainName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Request body for POST /drive/folders/content/{uuid}/files/existence
/// </summary>
public sealed class FileExistenceRequest
{
    [JsonPropertyName("files")]
    public List<FileNameAndType> Files { get; set; } = [];
}

/// <summary>
/// A name/type pair for the existence check
/// </summary>
public sealed class FileNameAndType
{
    [JsonPropertyName("plainName")]
    public string PlainName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Response from the existence check
/// </summary>
public sealed class FileExistenceResponse
{
    [JsonPropertyName("existentFiles")]
    public List<FileDto> ExistentFiles { get; set; } = [];
}

/// <summary>
/// Request body for POST /drive/storage/trash/add
/// </summary>
public sealed class TrashRequest
{
    [JsonPropertyName("items")]
    public List<TrashItem> Items { get; set; } = [];
}

/// <summary>
/// An item to move to the trash
/// </summary>
public sealed class TrashItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "file";
}

/// <summary>
/// Response from GET /drive/users/usage
/// </summary>
public sealed class UsageResponse
{
    [JsonPropertyName("drive")]
    public long Drive { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }
}

/// <summary>
/// Response from GET /drive/users/limit
/// </summary>
public sealed class LimitResponse
{
    [JsonPropertyName("maxSpaceBytes")]
    public long MaxSpaceBytes { get; set; }
}

/// <summary>
/// Response from GET /drive/files/limits
/// </summary>
public sealed class FileLimitsResponse
{
    [JsonPropertyName("maxUploadFileSize")]
    public long? MaxUploadFileSize { get; set; }
}
