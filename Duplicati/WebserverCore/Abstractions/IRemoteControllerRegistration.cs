namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// The registration of a remote control for this machine
/// </summary>
public interface IRemoteControllerRegistration
{
    /// <summary>
    /// Begin the registration of a machine
    /// </summary>
    /// <param name="registrationUrl">The URL to register the machine with</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The claim URL</returns>
    public Task<string> BeginRegisterMachine(string registrationUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the registration of the machine
    /// </summary>
    public void CancelRegisterMachine();

    /// <summary>
    /// Attempts to end the registration of a machine
    /// </summary>
    /// <returns>If the registration was successful</returns>
    public Task<bool> EndRegisterMachine();
}
