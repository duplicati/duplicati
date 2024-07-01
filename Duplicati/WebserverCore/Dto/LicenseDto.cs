namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The license dto
/// </summary>
public sealed record LicenseDto
{
    /// <summary>
    /// The component title
    /// </summary>
    public required string Title { get; init; }
    /// <summary>
    /// The homepage of the component
    /// </summary>
    public required string Url { get; init; }
    /// <summary>
    /// The license for the component
    /// </summary>
    public required string License { get; init; }
    /// <summary>
    /// The json data
    /// </summary>
    public required string Jsondata { get; init; }
}
