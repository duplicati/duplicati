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
using Google.Api.Gax.Grpc;
using Google.Api.Gax.Grpc.Rest;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.SecretManager.V1;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Implementation of a secret provider that reads secrets from Google Cloud Secret Manager
/// </summary>
public class GCSSecretProvider : ISecretProvider
{
    /// <summary>
    /// The prefix for extra options
    /// </summary>
    private const string OPTION_PREFIX_EXTRA = "ext-";

    /// <inheritdoc />
    public string Key => "gcsm";

    /// <inheritdoc />
    public string DisplayName => Strings.GCSSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.GCSSecretProvider.Description;

    /// <summary>
    /// The API types supported
    /// </summary>
    private enum ApiType
    {
        Grpc,
        Rest
    }

    /// <summary>
    /// The configuration for the secret provider
    /// </summary>
    private GCSSecretProviderOptions? _cfg;

    /// <summary>
    /// The options for the secret provider
    /// </summary>
    private class GCSSecretProviderOptions : ICommandLineArgumentMapper
    {
        public ApiType? ApiType { get; set; }
        public string? ProjectId { get; set; }
        public string? AccessToken { get; set; }
        public string Version { get; set; } = "latest";

        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(ApiType) => new CommandLineArgumentDescriptionAttribute() { Name = "api-type", Type = CommandLineArgument.ArgumentType.Enumeration, ShortDescription = Strings.GCSSecretProvider.ApiTypeDescriptionShort, LongDescription = Strings.GCSSecretProvider.ApiTypeDescriptionLong },
                nameof(ProjectId) => new CommandLineArgumentDescriptionAttribute() { Name = "project-id", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.GCSSecretProvider.ProjectIdDescriptionShort, LongDescription = Strings.GCSSecretProvider.ProjectIdDescriptionLong },
                nameof(AccessToken) => new CommandLineArgumentDescriptionAttribute() { Name = "access-token", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.GCSSecretProvider.AccessTokenDescriptionShort, LongDescription = Strings.GCSSecretProvider.AccessTokenDescriptionLong },
                nameof(Version) => new CommandLineArgumentDescriptionAttribute() { Name = "version", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.GCSSecretProvider.VersionDescriptionShort, LongDescription = Strings.GCSSecretProvider.VersionDescriptionLong },
                _ => null
            };

        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands =>
        CommandLineArgumentMapper.MapArguments(new GCSSecretProviderOptions())
        .Concat(CommandLineArgumentMapper.MapArguments(new SecretManagerServiceClientBuilder(), OPTION_PREFIX_EXTRA))
        .ToList();

    /// <summary>
    /// The client for the secret manager
    /// </summary>
    private SecretManagerServiceClient? _client;

    /// <summary>
    /// Gets the name of the argument
    /// </summary>
    /// <param name="name">The name of the argument</param>
    /// <returns>The name of the argument</returns>
    private string ArgName(string name) => GCSSecretProviderOptions.GetCommandLineArgumentDescription(name)?.Name ?? name;

    /// <inheritdoc />
    public async Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        var args = HttpUtility.ParseQueryString(config.Query);
        var builder = CommandLineArgumentMapper.ApplyArguments(new SecretManagerServiceClientBuilder(), args, OPTION_PREFIX_EXTRA);

        var cfg = CommandLineArgumentMapper.ApplyArguments(new GCSSecretProviderOptions(), args);
        if (string.IsNullOrWhiteSpace(cfg.ProjectId))
            throw new UserInformationException($"{ArgName(nameof(GCSSecretProviderOptions.ProjectId))} is required", "MissingProjectId");
        if (string.IsNullOrWhiteSpace(cfg.Version))
            throw new UserInformationException($"{ArgName(nameof(GCSSecretProviderOptions.Version))} is required", "MissingVersion");
        if (cfg.ApiType != null)
            builder.GrpcAdapter = cfg.ApiType switch
            {
                ApiType.Grpc => GrpcCoreAdapter.Instance,
                ApiType.Rest => RestGrpcAdapter.Default,
                _ => throw new UserInformationException($"Unknown API type: {cfg.ApiType}", "UnknownApiType")
            };

        if (!string.IsNullOrWhiteSpace(cfg.AccessToken))
            builder.Credential = GoogleCredential.FromAccessToken(cfg.AccessToken);
        else
            builder.Credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken).ConfigureAwait(false);

        var client = builder.Build();
        var res = client.ListSecretsAsync(new ListSecretsRequest() { ParentAsProjectName = new Google.Api.Gax.ResourceNames.ProjectName(cfg.ProjectId) }, CallSettings.FromCancellationToken(cancellationToken));
        await res.ReadPageAsync(1, cancellationToken).ConfigureAwait(false);
        _cfg = cfg;
        _client = client;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_client is null || _cfg is null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        var res = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var sec = await _client.AccessSecretVersionAsync(new AccessSecretVersionRequest() { SecretVersionName = new SecretVersionName(_cfg.ProjectId, key, "latest") }, callSettings: CallSettings.FromCancellationToken(cancellationToken)).ConfigureAwait(false);
            res[key] = sec.Payload.Data.ToStringUtf8();
        }

        return res;
    }
}
