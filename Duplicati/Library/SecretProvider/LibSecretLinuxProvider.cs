using System.Reflection;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Simple.CredentialManager;
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
        /// Gets the command line argument description for the given name
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <returns>The description or null if not found</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(Collection) => new CommandLineArgumentDescriptionAttribute() { Name = "collection", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.LibSecretLinuxProvider.CollectionDescriptionShort, LongDescription = Strings.LibSecretLinuxProvider.CollectionDescriptionLong },
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

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new LibSecretConfig())
            .ToList();

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LibSecret is only supported on Linux");

        var args = HttpUtility.ParseQueryString(config.Query);
        _cfg = CommandLineArgumentMapper.ApplyArguments(new LibSecretConfig(), args);
        if (string.IsNullOrWhiteSpace(_cfg.Collection))
            throw new UserInformationException("The collection must be specified", "CollectionRequired");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_cfg is null)
            throw new InvalidOperationException("The secret provider has not been initialized");
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LibSecret is only supported on Linux");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var value = new LinuxCredential(key, string.Empty, null, _cfg.Collection);
            if (!value.Load())
                throw new KeyNotFoundException($"The key '{key}' was not found");
            if (string.IsNullOrWhiteSpace(value.Password))
                throw new KeyNotFoundException($"The key '{key}' was found but the password is empty");

            result[key] = value.Password;
        }

        return Task.FromResult(result);
    }
}
