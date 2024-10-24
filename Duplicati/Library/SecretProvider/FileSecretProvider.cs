using System.Text.Json;
using System.Web;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from a file
/// </summary>
public class FileSecretProvider : ISecretProvider
{
    private const string PASSPHRASE_OPTION = "passphrase";

    /// <inheritdoc />
    public string Key => "file-secret";

    /// <inheritdoc />
    public string DisplayName => Strings.FileSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.FileSecretProvider.Description;

    /// <inheritdoc />
    private IReadOnlyDictionary<string, string>? _secrets;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [
        new CommandLineArgument(PASSPHRASE_OPTION, CommandLineArgument.ArgumentType.String, Strings.FileSecretProvider.PassphraseDescriptionShort, Strings.FileSecretProvider.PassphraseDescriptionLong, null),
    ];

    /// <inheritdoc />
    public async Task InitializeAsync(Uri config, CancellationToken cancellationToken)
    {
        var path = config.LocalPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        // Get the passphrase from the secrets-passphrase query parameter
        var args = HttpUtility.ParseQueryString(config.Query);
        var passphrase = args[PASSPHRASE_OPTION];
        using var fs = File.OpenRead(path);
        Dictionary<string, string> secrets;

        if (string.IsNullOrEmpty(passphrase))
        {
            secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fs, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The file does not contain any secrets");
        }
        else
        {
            using var ms = new MemoryStream();
            await SharpAESCrypt.AESCrypt.DecryptAsync(passphrase, fs, ms, SharpAESCrypt.DecryptionOptions.Default with { LeaveOpen = true }, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ms, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The file does not contain any secrets");
        }

        // Make secrets case-insensitive
        _secrets = secrets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase).AsReadOnly();
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_secrets is null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        return Task.FromResult(keys.ToDictionary(k => k, k => _secrets.TryGetValue(k, out var value) ? value : throw new KeyNotFoundException($"The key '{k}' was not found")));
    }
}
