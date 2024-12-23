namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The status of the remote control
/// </summary>
/// <param name="CanEnable">A flag indicating if the remote control can be enabled</param>
/// <param name="IsEnabled">A flag indicating if the remote control is enabled</param>
/// <param name="IsConnected">A flag indicating if the remote control is connected</param>
/// <param name="IsRegistering">A flag indicating if the remote control is registering</param>
/// <param name="RegistrationUrl">The URL to register the machine with</param>
public sealed record RemoteControlStatusOutput(
    bool CanEnable,
    bool IsEnabled,
    bool IsConnected,
    bool IsRegistering,
    bool IsRegisteringFaulted,
    bool IsRegisteringCompleted,
    string? RegistrationUrl
);
