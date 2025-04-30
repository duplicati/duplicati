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
using System.Text.Json;
using System.Web;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// A secret provider that retrieves secrets from AWS Secrets Manager
/// </summary>
public class AWSSecretProvider : ISecretProvider
{
    private const string OPTION_PREFIX_EXTRA = "ext-";

    /// <inheritdoc/>
    public string Key => "awssm";

    /// <inheritdoc/>
    public string DisplayName => Strings.AWSSecretProvider.DisplayName;

    /// <inheritdoc/>
    public string Description => Strings.AWSSecretProvider.Description;

    /// <summary>
    /// Constants for environment variables
    /// </summary>
    private static class EnvConstants
    {
        public const string AWS_ACCESS_KEY_ID = "AWS_ACCESS_KEY_ID";
        public const string AWS_SECRET_ACCESS_KEY = "AWS_SECRET_ACCESS_KEY";
        public const string AWS_DEFAULT_REGION = "AWS_DEFAULT_REGION";
        public const string AWS_ENDPOINT_URL = "AWS_ENDPOINT_URL";
    }

    /// <summary>
    /// Properties that are present in the specfic configuration and the general configuration
    /// </summary>
    private static readonly HashSet<string> DuplicatedProperties = new[] {
        nameof(AmazonSecretsManagerConfig.RegionEndpoint),
        nameof(AmazonSecretsManagerConfig.ServiceURL)
    }.ToHashSet();

    /// <summary>
    /// List of properties that slow down the loading of the AWS Secrets Manager client
    /// </summary>
    /// <remarks>Changes in this list will likely need to be reflected in S3AwsClient.cs</remarks>
    private static readonly HashSet<string> SlowLoadingProperties = new[] {
        nameof(AmazonSecretsManagerConfig.RegionEndpoint),
        nameof(AmazonSecretsManagerConfig.ServiceURL),
        nameof(AmazonSecretsManagerConfig.MaxErrorRetry),
        nameof(AmazonSecretsManagerConfig.DefaultConfigurationMode),
        nameof(AmazonSecretsManagerConfig.Timeout),
        nameof(AmazonSecretsManagerConfig.RetryMode),
    }.ToHashSet();

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new AWSSettings())
        .Concat(CommandLineArgumentMapper.MapArguments(new AmazonSecretsManagerConfig(), OPTION_PREFIX_EXTRA, exclude: DuplicatedProperties, excludeDefaultValue: SlowLoadingProperties))
        .ToList();

    /// <summary>
    /// The AWS Secrets Manager client; null if not initialized
    /// </summary>
    private AmazonSecretsManagerClient? _client;

    /// <summary>
    /// The secrets to fetch
    /// </summary>
    private string[] _secrets = Array.Empty<string>();

    /// <summary>
    /// Whether the secrets are case sensitive
    /// </summary>
    private bool _caseSensitive;

    /// <summary>
    /// Settings for AWS 
    /// </summary>
    private class AWSSettings : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The access key
        /// </summary>
        public string? AccessKey { get; set; }
        /// <summary>
        /// The secret key
        /// </summary>
        public string? SecretKey { get; set; }
        /// <summary>
        /// The region endpoint
        /// </summary>
        public string? RegionEndpoint { get; set; }
        /// <summary>
        /// The service URL
        /// </summary>
        public string? ServiceURL { get; set; }
        /// <summary>
        /// The secrets to fetch
        /// </summary>
        public string? Secrets { get; set; }
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
                nameof(AccessKey) => new CommandLineArgumentDescriptionAttribute() { Name = "access-key", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AWSSecretProvider.AccessKeyDescriptionShort, LongDescription = Strings.AWSSecretProvider.AccessKeyDescriptionLong(EnvConstants.AWS_ACCESS_KEY_ID) },
                nameof(SecretKey) => new CommandLineArgumentDescriptionAttribute() { Name = "secret-key", Type = CommandLineArgument.ArgumentType.Password, ShortDescription = Strings.AWSSecretProvider.SecretKeyDescriptionShort, LongDescription = Strings.AWSSecretProvider.SecretKeyDescriptionLong(EnvConstants.AWS_SECRET_ACCESS_KEY) },
                nameof(RegionEndpoint) => new CommandLineArgumentDescriptionAttribute() { Name = "region", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AWSSecretProvider.RegionEndpointDescriptionShort, LongDescription = Strings.AWSSecretProvider.RegionEndpointDescriptionLong(EnvConstants.AWS_DEFAULT_REGION) },
                nameof(ServiceURL) => new CommandLineArgumentDescriptionAttribute() { Name = "service-url", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AWSSecretProvider.ServiceURLDescriptionShort, LongDescription = Strings.AWSSecretProvider.ServiceURLDescriptionLong(EnvConstants.AWS_ENDPOINT_URL) },
                nameof(Secrets) => new CommandLineArgumentDescriptionAttribute() { Name = "secrets", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.AWSSecretProvider.SecretsDescriptionShort, LongDescription = Strings.AWSSecretProvider.SecretsDescriptionLong },
                nameof(CaseSensitive) => new CommandLineArgumentDescriptionAttribute() { Name = "case-sensitive", Type = CommandLineArgument.ArgumentType.Boolean, ShortDescription = Strings.AWSSecretProvider.CaseSensitiveDescriptionShort, LongDescription = Strings.AWSSecretProvider.CaseSensitiveDescriptionLong },
                _ => null
            };

        /// <inheritdoc/>
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <summary>
    /// Gets the name of the argument
    /// </summary>
    /// <param name="name">The name of the property</param>
    /// <returns>The name of the argument</returns>
    private string ArgName(string name) => AWSSettings.GetCommandLineArgumentDescription(name)?.Name ?? name;

    /// <inheritdoc/>
    public async Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        var args = HttpUtility.ParseQueryString(config.Query);
        var cred = CommandLineArgumentMapper.ApplyArguments(new AWSSettings(), args);

        if (string.IsNullOrWhiteSpace(cred.AccessKey))
            cred.AccessKey = Environment.GetEnvironmentVariable(EnvConstants.AWS_ACCESS_KEY_ID);
        if (string.IsNullOrWhiteSpace(cred.SecretKey))
            cred.SecretKey = Environment.GetEnvironmentVariable(EnvConstants.AWS_SECRET_ACCESS_KEY);
        if (string.IsNullOrWhiteSpace(cred.RegionEndpoint))
            cred.RegionEndpoint = Environment.GetEnvironmentVariable(EnvConstants.AWS_DEFAULT_REGION);
        if (string.IsNullOrWhiteSpace(cred.ServiceURL))
            cred.ServiceURL = Environment.GetEnvironmentVariable(EnvConstants.AWS_ENDPOINT_URL);

        if (string.IsNullOrWhiteSpace(cred.AccessKey) || string.IsNullOrWhiteSpace(cred.SecretKey))
            throw new UserInformationException($"{ArgName(nameof(AWSSettings.AccessKey))} and {ArgName(nameof(AWSSettings.AccessKey))} are required for {DisplayName}", "AwssmMissingCredentials");

        if (string.IsNullOrWhiteSpace(cred.RegionEndpoint) && string.IsNullOrWhiteSpace(cred.ServiceURL))
            throw new UserInformationException($"Either {ArgName(nameof(AWSSettings.RegionEndpoint))} or {ArgName(nameof(AWSSettings.ServiceURL))} is required for {DisplayName}", "AwssmMissingRegionOrUrl");

        if (string.IsNullOrWhiteSpace(cred.Secrets))
            throw new UserInformationException($"{ArgName(nameof(AWSSettings.Secrets))} is required for {DisplayName}", "AwssmMissingSecrets");
        _secrets = cred.Secrets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        _caseSensitive = cred.CaseSensitive;

        var initssconf = new AmazonSecretsManagerConfig();
        if (!string.IsNullOrWhiteSpace(cred.RegionEndpoint))
            initssconf.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(cred.RegionEndpoint);
        if (!string.IsNullOrWhiteSpace(cred.ServiceURL))
            initssconf.ServiceURL = cred.ServiceURL;

        var scconfig = CommandLineArgumentMapper.ApplyArguments(
            initssconf,
            args,
            OPTION_PREFIX_EXTRA
        );

        var credentials = new Amazon.Runtime.BasicAWSCredentials(cred.AccessKey, cred.SecretKey);
        var client = new AmazonSecretsManagerClient(credentials, scconfig);

        // Test the connection
        await client.ListSecretsAsync(new ListSecretsRequest()
        {
            MaxResults = 1
        }, cancellationToken).ConfigureAwait(false);

        _client = client;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_client == null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        var result = new Dictionary<string, string>();
        var missing = new HashSet<string>(keys);

        foreach (var secret in _secrets)
        {
            var response = await _client.GetSecretValueAsync(new GetSecretValueRequest()
            {
                SecretId = secret
            }, cancellationToken).ConfigureAwait(false);

            var secretString = response.SecretString;
            if (string.IsNullOrWhiteSpace(secretString) && response.SecretBinary != null)
            {
                using (var ms = response.SecretBinary)
                using (var sr = new StreamReader(ms))
                    secretString = await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            var values = string.IsNullOrWhiteSpace(secretString) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(response.SecretString);
            if (values != null)
            {
                if (!_caseSensitive)
                    values = values
                        .GroupBy(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);


                foreach (var key in missing)
                {
                    if (values.TryGetValue(key, out var value) && value is string stringValue)
                    {
                        result[key] = stringValue;
                        missing.Remove(key);
                    }
                }

                if (missing.Count == 0)
                    return result;
            }
        }

        throw new KeyNotFoundException("The following keys were not found: " + string.Join(", ", missing));

    }
}