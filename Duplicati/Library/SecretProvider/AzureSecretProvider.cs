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
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from Azure Key Vault
/// </summary>
public class AzureSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "azkv";

    /// <inheritdoc />
    public string DisplayName => Strings.AzureSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.AzureSecretProvider.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new AzureSecretProviderConfig())
            .ToList();

    public enum ConnectionType
    {
        Https,
        Http
    }

    public enum AuthenticationType
    {
        ClientSecret,
        ManagedIdentity,
        UsernamePassword
    }

    private class AzureSecretProviderConfig : ICommandLineArgumentMapper
    {
        public string? KeyVaultName { get; set; }
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Https;
        public string? VaultUri { get; set; }
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public AuthenticationType AuthenticationType { get; set; } = AuthenticationType.ManagedIdentity;

        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(KeyVaultName) => new CommandLineArgumentDescriptionAttribute() { Name = "keyvault-name", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AzureSecretProvider.KeyVaultNameDescriptionShort, LongDescription = Strings.AzureSecretProvider.KeyVaultNameDescriptionLong },
                nameof(ConnectionType) => new CommandLineArgumentDescriptionAttribute() { Name = "connection-type", Type = CommandLineArgument.ArgumentType.Enumeration, ShortDescription = Strings.AzureSecretProvider.ConnectionTypeDescriptionShort, LongDescription = Strings.AzureSecretProvider.ConnectionTypeDescriptionLong },
                nameof(VaultUri) => new CommandLineArgumentDescriptionAttribute() { Name = "vault-uri", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AzureSecretProvider.VaultUriDescriptionShort, LongDescription = Strings.AzureSecretProvider.VaultUriDescriptionLong },
                nameof(TenantId) => new CommandLineArgumentDescriptionAttribute() { Name = "tenant-id", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AzureSecretProvider.TenantIdDescriptionShort, LongDescription = Strings.AzureSecretProvider.TenantIdDescriptionLong },
                nameof(ClientId) => new CommandLineArgumentDescriptionAttribute() { Name = "client-id", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AzureSecretProvider.ClientIdDescriptionShort, LongDescription = Strings.AzureSecretProvider.ClientIdDescriptionLong },
                nameof(ClientSecret) => new CommandLineArgumentDescriptionAttribute() { Name = "client-secret", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.AzureSecretProvider.ClientSecretDescriptionShort, LongDescription = Strings.AzureSecretProvider.ClientSecretDescriptionLong },
                nameof(Username) => new CommandLineArgumentDescriptionAttribute() { Name = "username", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AzureSecretProvider.UsernameDescriptionShort, LongDescription = Strings.AzureSecretProvider.UsernameDescriptionLong },
                nameof(Password) => new CommandLineArgumentDescriptionAttribute() { Name = "password", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.AzureSecretProvider.PasswordDescriptionShort, LongDescription = Strings.AzureSecretProvider.PasswordDescriptionLong },
                nameof(AuthenticationType) => new CommandLineArgumentDescriptionAttribute() { Name = "auth-type", Type = CommandLineArgument.ArgumentType.Enumeration, ShortDescription = Strings.AzureSecretProvider.AuthenticationTypeDescriptionShort, LongDescription = Strings.AzureSecretProvider.AuthenticationTypeDescriptionLong },
                _ => null
            };

        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <summary>
    /// The Azure Key Vault client; null if not initialized
    /// </summary>
    private SecretClient? _client;

    /// <summary>
    /// Gets the name of the argument
    /// </summary>
    /// <param name="name">The name of the argument</param>
    /// <returns>The name of the argument</returns>
    private string ArgName(string name) => AzureSecretProviderConfig.GetCommandLineArgumentDescription(name)?.Name ?? name;

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        var args = HttpUtility.ParseQueryString(config.Query);
        var cfg = CommandLineArgumentMapper.ApplyArguments(new AzureSecretProviderConfig(), args);
        var scheme = cfg.ConnectionType == ConnectionType.Http ? "http" : "https";

        if (string.IsNullOrWhiteSpace(cfg.KeyVaultName) && string.IsNullOrWhiteSpace(cfg.VaultUri))
            throw new UserInformationException($"Either {ArgName(nameof(AzureSecretProviderConfig.KeyVaultName))} or {ArgName(nameof(AzureSecretProviderConfig.VaultUri))} is required", "MissingVaultNameOrUri");
        else if (!string.IsNullOrWhiteSpace(cfg.KeyVaultName) && !string.IsNullOrWhiteSpace(cfg.VaultUri))
            throw new UserInformationException($"Only one of {ArgName(nameof(AzureSecretProviderConfig.KeyVaultName))} or {ArgName(nameof(AzureSecretProviderConfig.VaultUri))} can be specified", "BothVaultNameAndUriSpecified");

        var vaultUri = cfg.VaultUri ?? $"{scheme}://{cfg.KeyVaultName}.vault.azure.net";

        if (cfg.AuthenticationType == AuthenticationType.ClientSecret && (string.IsNullOrWhiteSpace(cfg.TenantId) || string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret)))
            throw new UserInformationException($"The settings {ArgName(nameof(AzureSecretProviderConfig.TenantId))}, {ArgName(nameof(AzureSecretProviderConfig.ClientId))}, and {ArgName(nameof(AzureSecretProviderConfig.ClientSecret))} are required for client secret authentication", "MissingClientSecretSettings");
        else if (cfg.AuthenticationType == AuthenticationType.UsernamePassword && (string.IsNullOrWhiteSpace(cfg.TenantId) || string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.Username) || string.IsNullOrWhiteSpace(cfg.Password)))
            throw new UserInformationException($"The settings {ArgName(nameof(AzureSecretProviderConfig.TenantId))}, {ArgName(nameof(AzureSecretProviderConfig.ClientId))}, {ArgName(nameof(AzureSecretProviderConfig.Username))}, and {ArgName(nameof(AzureSecretProviderConfig.Password))} are required for username/password authentication", "MissingUsernamePasswordSettings");

        TokenCredential credential = cfg.AuthenticationType switch
        {
            AuthenticationType.ClientSecret => new ClientSecretCredential(cfg.TenantId, cfg.ClientId, cfg.ClientSecret),
            AuthenticationType.ManagedIdentity => new DefaultAzureCredential(),
            AuthenticationType.UsernamePassword => new UsernamePasswordCredential(cfg.Username, cfg.Password, cfg.TenantId, cfg.ClientId),
            _ => throw new UserInformationException($"Authentication type {cfg.AuthenticationType} is not supported", "UnsupportedAuthenticationType")
        };


        _client = new SecretClient(new System.Uri(vaultUri), credential);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        var secrets = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var secret = await _client.GetSecretAsync(key, cancellationToken: cancellationToken).ConfigureAwait(false);
            secrets[key] = secret.Value.Value;
        }

        return secrets;
    }
}
