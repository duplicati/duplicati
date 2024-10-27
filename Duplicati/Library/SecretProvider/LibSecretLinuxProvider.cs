using System.Reflection;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.SecretProvider.LibSecret;
using Duplicati.Library.Utility;
namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from libsecret on Linux
/// </summary>
public class LibSecretLinuxProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "libsecret";

    /// <inheritdoc />
    public string DisplayName => Strings.LibSecretLinuxProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.LibSecretLinuxProvider.Description;

    /// <summary>
    /// The configuration for the secret provider
    /// </summary>
    private class LibSecretConfig : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The collection to use
        /// </summary>
        public string Collection { get; set; } = "default";

        /// <summary>
        /// Whether the collection name is case sensitive
        /// </summary>
        public bool CaseSensitive { get; set; }

        /// <summary>
        /// Gets the command line argument description for the given name
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <returns>The description or null if not found</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(Collection) => new CommandLineArgumentDescriptionAttribute() { Name = "collection", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.LibSecretLinuxProvider.CollectionDescriptionShort, LongDescription = Strings.LibSecretLinuxProvider.CollectionDescriptionLong },
                nameof(CaseSensitive) => new CommandLineArgumentDescriptionAttribute() { Name = "case-sensitive", Type = CommandLineArgument.ArgumentType.Boolean, ShortDescription = Strings.LibSecretLinuxProvider.CaseSensitiveDescriptionShort, LongDescription = Strings.LibSecretLinuxProvider.CaseSensitiveDescriptionLong },
                _ => null,
            };

        /// <inheritdoc />
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <summary>
    /// The configuration for the secret provider; null if not initialized
    /// </summary>
    private LibSecretConfig? _cfg;

    /// <summary>
    /// The secret collection
    /// </summary>
    private SecretCollection? _collection;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new LibSecretConfig())
            .ToList();

    /// <inheritdoc />
    public async Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LibSecret is only supported on Linux");

        var args = HttpUtility.ParseQueryString(config.Query);
        _cfg = CommandLineArgumentMapper.ApplyArguments(new LibSecretConfig(), args);
        if (string.IsNullOrWhiteSpace(_cfg.Collection))
            throw new UserInformationException("The collection must be specified", "CollectionRequired");

        _collection = await SecretCollection.CreateAsync(_cfg.Collection, cancellationToken).ConfigureAwait(false);
        await _collection.UnlockAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_cfg is null || _collection is null)
            throw new InvalidOperationException("The secret provider has not been initialized");
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LibSecret is only supported on Linux");

        return _collection.GetSecretsAsync(keys, _cfg.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    }
}
