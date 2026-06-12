namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// The request object for the destination list endpoint.
/// </summary>
public class DestinationListRequestDto
{
    /// <summary>
    /// The backup ID to list destination content for.
    /// </summary>
    public required string? BackupId { get; set; }

    /// <summary>
    /// The connection string ID, if known
    /// </summary>
    public long? ConnectionStringId { get; init; }

    /// <summary>
    /// The destination URL to list destination content for.
    /// </summary>
    public required string DestinationUrl { get; set; }

    /// <summary>
    /// The type of remote destination
    /// </summary>
    public RemoteDestinationType? DestinationType { get; init; }

    /// <summary>
    /// The source prefix to identify the source provider connection string
    /// </summary>
    public string? SourcePrefix { get; init; }

    /// <summary>
    /// The path to list destination content for.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The offset to start listing from.
    /// </summary>
    public required int? Offset { get; set; }

    /// <summary>
    /// The maximum number of items to return.
    /// </summary>
    public required int? Limit { get; set; }
}
