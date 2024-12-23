namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The backup DTO
/// </summary>
public sealed record BackupDto
{
    /// <summary>
    /// The backup ID
    /// </summary>
    public required string ID { get; init; }
    /// <summary>
    /// The backup name
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The backup description
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// The backup tags
    /// </summary>
    public required string[] Tags { get; init; }
    /// <summary>
    /// The backup target url
    /// </summary>
    public required string TargetURL { get; init; }
    /// <summary>
    /// The path to the local database
    /// </summary>
    public required string DBPath { get; init; }

    /// <summary>
    /// The backup source folders and files
    /// </summary>
    public required string[] Sources { get; init; }

    /// <summary>
    /// The backup settings
    /// </summary>
    public required IEnumerable<SettingDto>? Settings { get; init; }

    /// <summary>
    /// The filters applied to the source files
    /// </summary>
    public required IEnumerable<FilterDto>? Filters { get; init; }

    /// <summary>
    /// The backup metadata
    /// </summary>
    public required IDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets a value indicating if this instance is not persisted to the database
    /// </summary>
    public required bool IsTemporary { get; init; }

    /// <summary>
    /// Gets a value indicating if backup is unencrypted or passphrase is stored
    /// </summary>
    public required bool IsUnencryptedOrPassphraseStored { get; init; }
}
