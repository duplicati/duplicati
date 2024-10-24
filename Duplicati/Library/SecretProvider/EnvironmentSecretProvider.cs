using Duplicati.Library.Interface;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from environment variables
/// </summary>
public class EnvironmentSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "env";

    /// <inheritdoc />
    public string DisplayName => Strings.EnvironmentSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.EnvironmentSecretProvider.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [];

    /// <inheritdoc />
    public Task InitializeAsync(Uri config, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var env = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
        var envLookup = env.Keys.Cast<string>().ToDictionary(k => k, k => env[k] as string, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(
            keys.ToDictionary(
                k => k,
                k =>
                {
                    var value = envLookup.GetValueOrDefault(k);
                    if (string.IsNullOrWhiteSpace(value))
                        throw new KeyNotFoundException($"The key '{k}' was not found");
                    return value;
                }
            )
        );
    }
}
