using Duplicati.Library.Interface;

namespace Duplicati.Library.SecretProvider;

public static class SecretProviderModules
{
    /// <summary>
    /// The list of all built-in secret provider modules
    /// </summary>
    public static IReadOnlyList<ISecretProvider> Modules => [
        new EnvironmentSecretProvider(),
        new FileSecretProvider(),
        new AWSSecretProvider(),
        new HCVaultSecretProvider(),
        new GCSSecretProvider(),
        new AzureSecretProvider(),
    ];
}
