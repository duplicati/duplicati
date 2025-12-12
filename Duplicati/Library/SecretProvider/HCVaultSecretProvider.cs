// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System.Reflection;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// A secret provider that retrieves secrets from HashiCorp Vault
/// </summary>
public class HCVaultSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "hcv";

    /// <inheritdoc />
    public string DisplayName => Strings.HCVaultSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.HCVaultSecretProvider.Description;

    /// <inheritdoc />
    public Task<bool> IsSupported(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public bool IsSetSupported => true;

    /// <summary>
    /// The configuration for the secret provider; null if not initialized
    /// </summary>
    private IVaultClient? _client;

    /// <summary>
    /// The list of secrets to fetch
    /// </summary>
    private IReadOnlyList<string>? _secrets;

    /// <summary>
    /// Whether the secrets are case sensitive
    /// </summary>
    private bool _caseSensitive;

    /// <summary>
    /// The mount point to use
    /// </summary>
    private string? _mountPoint;

    /// <summary>
    /// Constants for environment variables
    /// </summary>
    private static class EnvConstants
    {
        /// <summary>
        /// The client ID for the HashiCorp Vault
        /// </summary>
        public const string HCP_CLIENT_ID = "HCP_CLIENT_ID";
        /// <summary>
        /// The client secret for the HashiCorp Vault
        /// </summary>
        public const string HCP_CLIENT_SECRET = "HCP_CLIENT_SECRET";
    }

    /// <summary>
    /// The connection types
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// Use HTTPS
        /// </summary>
        Https,
        /// <summary>
        /// Use HTTP
        /// </summary>
        Http
    };

    /// <summary>
    /// Mapper for the command line arguments
    /// </summary>
    private class HCVaultSettings : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The token to use for authentication
        /// </summary>
        public string? Token { get; set; }
        /// <summary>
        /// The connection type to use
        /// </summary>
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Https;
        /// <summary>
        /// The secrets to probe for values
        /// </summary>
        public string? Secrets { get; set; }
        /// <summary>
        /// The mount point for the secrets
        /// </summary>
        public string? MountPoint { get; set; } = "secret";
        /// <summary>
        /// The client ID to use for authentication
        /// </summary>
        public string? ClientId { get; set; }
        /// <summary>
        /// The client secret to use for authentication
        /// </summary>
        public string? ClientSecret { get; set; }
        /// <summary>
        /// Whether the secrets are case sensitive
        /// </summary>
        public bool CaseSensitive { get; set; }

        /// <summary>
        /// Gets the description for a command line argument
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <returns>The description for the argument</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(Token) => new CommandLineArgumentDescriptionAttribute() { Name = "token", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.HCVaultSecretProvider.TokenDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.TokenDescriptionLong },
                nameof(ConnectionType) => new CommandLineArgumentDescriptionAttribute() { Name = "connection-type", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.HCVaultSecretProvider.ProtocolDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.ProtocolDescriptionLong },
                nameof(Secrets) => new CommandLineArgumentDescriptionAttribute() { Name = "secrets", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.HCVaultSecretProvider.SecretsDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.SecretsDescriptionLong },
                nameof(ClientId) => new CommandLineArgumentDescriptionAttribute() { Name = "client-id", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.HCVaultSecretProvider.ClientIdDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.ClientIdDescriptionLong(EnvConstants.HCP_CLIENT_ID) },
                nameof(ClientSecret) => new CommandLineArgumentDescriptionAttribute() { Name = "client-secret", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.HCVaultSecretProvider.ClientSecretDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.ClientSecretDescriptionLong(EnvConstants.HCP_CLIENT_SECRET) },
                nameof(MountPoint) => new CommandLineArgumentDescriptionAttribute() { Name = "mount", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.HCVaultSecretProvider.MountPointDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.MountPointDescriptionLong },
                nameof(CaseSensitive) => new CommandLineArgumentDescriptionAttribute() { Name = "case-sensitive", Type = CommandLineArgument.ArgumentType.Boolean, ShortDescription = Strings.HCVaultSecretProvider.CaseSensitiveDescriptionShort, LongDescription = Strings.HCVaultSecretProvider.CaseSensitiveDescriptionLong },
                _ => null
            };

        /// <inheritdoc/>
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new HCVaultSettings())
            .Concat(CommandLineArgumentMapper.MapArguments(typeof(VaultClientSettings)))
            .ToList();

    /// <summary>
    /// Gets the name of the argument
    /// </summary>
    /// <param name="name">The name of the argument</param>
    /// <returns>The name of the argument</returns>
    private string ArgName(string name) => HCVaultSettings.GetCommandLineArgumentDescription(name)?.Name ?? name;

    /// <inheritdoc />
    public async Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        var args = HttpUtility.ParseQueryString(config.Query);
        var cfg = CommandLineArgumentMapper.ApplyArguments(new HCVaultSettings(), args);

        if (string.IsNullOrWhiteSpace(cfg.ClientId))
            cfg.ClientId = Environment.GetEnvironmentVariable(EnvConstants.HCP_CLIENT_ID);
        if (string.IsNullOrWhiteSpace(cfg.ClientSecret))
            cfg.ClientSecret = Environment.GetEnvironmentVariable(EnvConstants.HCP_CLIENT_SECRET);

        if (string.IsNullOrWhiteSpace(cfg.ClientSecret) && !string.IsNullOrWhiteSpace(cfg.ClientId))
            throw new UserInformationException($"{ArgName(nameof(HCVaultSettings.ClientSecret))} is required when {ArgName(nameof(HCVaultSettings.ClientId))} is specified", "MissingClientSecret");
        if (string.IsNullOrWhiteSpace(cfg.Token) && string.IsNullOrWhiteSpace(cfg.ClientId))
            throw new UserInformationException($"Either {ArgName(nameof(HCVaultSettings.Token))} or {ArgName(nameof(HCVaultSettings.ClientId))} is required", "MissingTokenOrClient");
        if (string.IsNullOrWhiteSpace(cfg.Secrets))
            throw new UserInformationException($"{ArgName(nameof(HCVaultSettings.Secrets))} is required", "MissingSecrets");

        var secrets = cfg.Secrets?.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();

        var builder = new UriBuilder(config)
        {
            Scheme = cfg.ConnectionType == ConnectionType.Http ? "http" : "https",
            Query = null
        };

        IAuthMethodInfo authMethod = string.IsNullOrWhiteSpace(cfg.Token)
            ? new AppRoleAuthMethodInfo(cfg.ClientId, cfg.ClientSecret)
            : new TokenAuthMethodInfo(cfg.Token);

        var vaultConfig = CommandLineArgumentMapper.ApplyArguments(
            new VaultClientSettings(builder.Uri.ToString(), authMethod),
            args
        );

        var client = new VaultClient(vaultConfig);
        // Check if the connection works
        await client.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: secrets.First(), mountPoint: cfg.MountPoint).ConfigureAwait(false); //missing cancellationToken

        _secrets = secrets;
        _mountPoint = cfg.MountPoint;
        _caseSensitive = cfg.CaseSensitive;
        _client = client;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_client is null || _secrets is null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        var comparer = _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var result = new Dictionary<string, string>(comparer);
        var missing = new HashSet<string>(keys, comparer);

        foreach (var secret in _secrets)
        {
            try
            {
                var data = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(secret, mountPoint: _mountPoint).ConfigureAwait(false);
                var lookup = data?.Data?.Data;
                if (lookup is null)
                    continue;

                foreach (var kvp in lookup)
                {
                    if (kvp.Value is string value)
                        result[kvp.Key] = value;
                }

                foreach (var key in missing.ToList())
                {
                    if (result.ContainsKey(key))
                        missing.Remove(key);
                }

                if (missing.Count == 0)
                    return result;
            }
            catch (VaultApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }
        }

        foreach (var key in missing.ToList())
        {
            try
            {
                var response = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(key, mountPoint: _mountPoint).ConfigureAwait(false);
                var data = response?.Data?.Data;
                if (data is null)
                    continue;

                if (data.TryGetValue(key, out var value) && value is string strValue)
                {
                    result[key] = strValue;
                    missing.Remove(key);
                }
                else if (data.Count == 1 && data.First().Value is string onlyValue)
                {
                    result[key] = onlyValue;
                    missing.Remove(key);
                }
            }
            catch (VaultApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Ignore and continue looking for other keys
            }
        }

        if (missing.Count > 0)
            throw new KeyNotFoundException("The following keys were not found: " + string.Join(", ", missing));

        return result;
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken)
    {
        if (_client is null || string.IsNullOrWhiteSpace(_mountPoint))
            throw new InvalidOperationException("The secret provider has not been initialized");

        var exists = true;
        try
        {
            await _client.V1.Secrets.KeyValue.V2.ReadSecretMetadataAsync(key, _mountPoint).ConfigureAwait(false);
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            exists = false;
        }

        if (exists && !overwrite)
            throw new UserInformationException($"The key '{key}' already exists", "KeyAlreadyExists");

        var payload = new Dictionary<string, object>
        {
            [key] = value
        };

        await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(key, payload, null, _mountPoint).ConfigureAwait(false);
    }
}
