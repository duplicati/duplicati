namespace Duplicati.WebserverCore.Dto
{
    /// <summary>
    /// Represents the backup defaults DTO.
    /// </summary>
    public sealed record BackupDefaultsDto
    {
        /// <summary>
        /// Gets the backup defaults
        /// </summary>
        public object? data { get; init; }
    }
}