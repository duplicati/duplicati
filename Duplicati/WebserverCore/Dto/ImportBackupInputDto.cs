namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The import backup input DTO
/// </summary>
/// <param name="config">The base64 encoded backup configuration</param>
/// <param name="cmdline">Whether the backup should be imported from a command line</param>
/// <param name="import_metadata">Whether the metadata should be imported</param>
/// <param name="direct">Whether the backup should be imported and created directly</param>
/// <param name="passphrase">The passphrase to use for the backup configuration</param>
public sealed record ImportBackupInputDto
(
    string config,
    bool? cmdline,
    bool? import_metadata,
    bool? direct,
    string? passphrase
);