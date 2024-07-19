namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The filter DTO
/// </summary>
public sealed record FilterDto
{
    /// <summary>
    /// The sort order
    /// </summary>
    public required long Order { get; init; }

    /// <summary>
    /// True if the filter includes the items, false if it excludes
    /// </summary>
    public required bool Include { get; init; }

    /// <summary>
    /// The filter expression.
    /// If the filter is a regular expression, it starts and ends with hard brackets [ ]
    /// </summary>
    public required string Expression { get; init; }
}
