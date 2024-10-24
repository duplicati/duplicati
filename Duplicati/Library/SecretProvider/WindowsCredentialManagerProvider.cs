using Duplicati.Library.Interface;
using Meziantou.Framework.Win32;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from Windows Credential Manager
/// </summary>
public class WindowsCredentialManagerProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "wincred";

    /// <inheritdoc />
    public string DisplayName => Strings.WindowsCredentialManagerProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.WindowsCredentialManagerProvider.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [];

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows");
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows XP or later");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var cred = CredentialManager.ReadCredential(key);
            var value = cred?.Password;
            if (string.IsNullOrWhiteSpace(value))
                throw new KeyNotFoundException($"The key '{key}' was not found");

            result[key] = value;
        }

        return Task.FromResult(result);
    }
}
