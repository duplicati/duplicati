namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// DTO for the restore endpoint
/// </summary>
/// <param name="paths">The paths to restore</param>
/// <param name="passphrase">The passphrase to use for decryption</param>
/// <param name="time">The time to restore to</param>
/// <param name="restore_path">The path to restore to</param>
/// <param name="overwrite">Whether to overwrite existing files</param>
/// <param name="permissions">Whether to restore permissions</param>
/// <param name="skip_metadata">Whether to skip metadata</param>
public sealed record RestoreInputDto(string[]? paths, string? passphrase, string time, string? restore_path, bool? overwrite, bool? permissions, bool? skip_metadata);
