namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The setting DTO
/// </summary>
public sealed record SettingDto
{
    /// <summary>
    /// The filter expression
    /// </summary>
    public required string? Filter { get; init; }
    /// <summary>
    /// The setting option
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The setting value
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The actual option arguments
    /// </summary>
    public required Library.Interface.ICommandLineArgument? Argument { get; init; }
}
