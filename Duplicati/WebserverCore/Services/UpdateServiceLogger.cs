namespace Duplicati.WebserverCore.Services;

public static partial class UpdateServiceLogger
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "Could not deserialize last update version information `{Version}`")]
    public static partial void CouldNotDeserialize(
        ILogger logger, string version);
}