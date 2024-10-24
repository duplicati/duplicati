#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main;

/// <summary>
/// Helper to apply secret provider to arguments
/// </summary>
public static class SecretProviderHelper
{
    /// <summary>
    /// The log tag
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<SecretProviderLoader>();

    /// <summary>
    /// The different levels of caching permitted for the secrets
    /// </summary>
    public enum CachingLevel
    {
        /// <summary>
        /// Values are always fetched from the provider
        /// </summary>
        None,
        /// <summary>
        /// Values are cached in memory and used if the provider is not available
        /// </summary>
        InMemory,
        /// <summary>
        /// Values are cached in memory and saved to disk with encryption.
        /// If the provider is not available, the values are fetched from disk.
        /// </summary>
        Persistent
    }

    /// <summary>
    /// Creates an instance of a secret provider with caching enabled
    /// </summary>
    /// <param name="config">The configuration string</param>
    /// <param name="cachingLevel">The caching level</param>
    /// <param name="persistedFolder">The folder to persist the cache to</param>
    /// <param name="salt">The salt to use for hashing</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The secret provider instance</returns>
    public static async Task<ISecretProvider> CreateInstanceAsync(string config, CachingLevel cachingLevel, string persistedFolder, string salt, CancellationToken cancelToken)
    {
        var provider = SecretProviderLoader.CreateInstance(config);
        var sp = new SecretProviderCached(config, provider, cachingLevel, persistedFolder, salt);
        await sp.InitializeAsync(new System.Uri(config), cancelToken).ConfigureAwait(false);

        return sp;
    }

    /// <summary>
    /// Applies the secret provider to the arguments.
    /// Note that this method modifes the arguments and options in place.
    /// </summary>
    /// <param name="arguments">The arguments to modify</param>
    /// <param name="options">The options to modify</param>
    /// <param name="persistedFolder">The persisted secret cache folder</param>
    /// <param name="fallbackProvider">The fallback provider to use if no provider is specified</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The secret provider</returns>
    public static async Task<ISecretProvider?> ApplySecretProviderAsync(string?[] arguments, Dictionary<string, string?> options, string persistedFolder, ISecretProvider? fallbackProvider, CancellationToken cancellationToken)
    {
        var provider = options.GetValueOrDefault("secret-provider");
        if (string.IsNullOrWhiteSpace(provider) && fallbackProvider == null)
            return null;

        var cachingLevel = Library.Utility.Utility.ParseEnumOption(options, "secret-provider-cache", CachingLevel.None);

        // Weak salt, but semi-static
        ISecretProvider secretProvider;
        if (string.IsNullOrWhiteSpace(provider))
        {
            secretProvider = fallbackProvider
                ?? throw new InvalidOperationException("No secret provider specified");
        }
        else
        {
            var newProvider = SecretProviderLoader.CreateInstance(provider);

            string salt;
            using (var hasher = HashFactory.CreateHasher(HashFactory.SHA256))
                salt = Environment.MachineName.ComputeHashToHex(hasher);

            secretProvider = new SecretProviderCached(provider, newProvider, cachingLevel, persistedFolder, salt);
            await secretProvider.InitializeAsync(new System.Uri(provider), cancellationToken).ConfigureAwait(false);
        }

        var pattern = options.GetValueOrDefault("secret-provider-pattern", "$");
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException("No secret provider pattern specified");

        await ReplaceSecretsAsync(secretProvider, arguments, options, pattern, cancellationToken).ConfigureAwait(false);
        return secretProvider;
    }

    /// <summary>
    /// Helper method that finds all secrets matching the prefix and replaces them with the resolved values
    /// </summary>
    /// <param name="provider">The secret provider to use</param>
    /// <param name="values">Any string values to update</param>
    /// <param name="options">Any options to update</param>
    /// <param name="matchpattern">The prefix to look for</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public static async Task ReplaceSecretsAsync(ISecretProvider provider, string?[] values, Dictionary<string, string?> options, string matchpattern, CancellationToken cancelToken)
    {
        // Unwrap ${} to support ${name is long}
        var suffix = string.Empty;
        var matcher = @"\w";
        if (matchpattern.EndsWith("{}") || matchpattern.EndsWith("()") || matchpattern.EndsWith("[]"))
        {
            suffix = matchpattern[^1..];
            matchpattern = matchpattern[..^1];
            matcher = @"[^" + Regex.Escape(suffix) + "]";
        }

        // For the values, they could be urls, so we need to look inside the strings
        var pattern = new Regex(@$"{Regex.Escape(matchpattern)}(?<key>{matcher}+){Regex.Escape(suffix)}", RegexOptions.ExplicitCapture);

        // When we get the secrets, replace these values
        var optionsMap = options
            .Where(x => !x.Key.StartsWith("secret-provider", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => (x.Key, Value: x.Value!, Match: pattern.Match(x.Value!)))
            .Where(x => x.Match.Success && !string.IsNullOrWhiteSpace(x.Match.Groups["key"].Value) && x.Match.Length == x.Value.Length)
            .Select(x => (x.Key, Secret: x.Match.Groups["key"].Value))
            .Where(x => !string.IsNullOrWhiteSpace(x.Secret))
            .GroupBy(x => x.Secret)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Key).ToArray());

        var secrets = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => pattern.Matches(x!).Select(m => m.Groups["key"].Value))
            .Concat(optionsMap.Keys)
            .Distinct()
            .ToArray();

        if (secrets.Length == 0)
            return;

        var translated = await provider.ResolveSecretsAsync(secrets, cancelToken).ConfigureAwait(false);

        // Update options by replacing values
        foreach (var v in optionsMap)
            foreach (var k in v.Value)
                options[k] = translated[v.Key];

        // Update values by replacing within the strings
        for (var i = 0; i < values.Length; i++)
        {
            var prev = values[i];
            if (!string.IsNullOrWhiteSpace(prev))
                values[i] = pattern.Replace(prev, m => translated[m.Groups["key"].Value]);
        }

        return;
    }


    /// <summary>
    /// A cache for secret provider values
    /// </summary>
    private class SecretProviderCached : ISecretProvider
    {
        /// <summary>
        /// The provider being cached
        /// </summary>
        private readonly ISecretProvider _provider;
        /// <summary>
        /// A flag indicating if the provider has been initialized
        /// </summary>
        private bool _initialized;
        /// <summary>
        /// The caching level
        /// </summary>
        private readonly CachingLevel _cachingLevel;
        /// <summary>
        /// The configuration string
        /// </summary>
        private readonly string _config;
        /// <summary>
        /// The salt used for hashing and uniqueness
        /// </summary>
        private readonly string _salt;
        /// <summary>
        /// The persisted file; null if not persistent
        /// </summary>
        private readonly string? _persistedFile;
        /// <summary>
        /// The passphrase used to encrypt the persisted file
        /// </summary>
        private readonly string? _passphrase;
        /// <summary>
        /// The lock object guarding _cache
        /// </summary>
        private static readonly object _lock = new();
        /// <summary>
        /// The in-memory cache of secrets
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

        /// <summary>
        /// Creates a new instance of the secret provider cache
        /// </summary>
        /// <param name="config">The configuration string</param>
        /// <param name="provider">The provider to cache</param>
        /// <param name="cachingLevel">The caching level</param>
        /// <param name="persistedFolder">The folder to persist the cache to</param>
        /// <param name="salt">The salt to use for hashing</param>
        public SecretProviderCached(string config, ISecretProvider provider, CachingLevel cachingLevel, string persistedFolder, string salt)
        {
            _provider = provider;
            _cachingLevel = cachingLevel;
            _config = config;
            _salt = salt;
            if (cachingLevel == CachingLevel.Persistent)
            {
                // Create a unique file name for the cache, tied to the configuration
                var name = Convert.ToBase64String(Library.Utility.Utility.RepeatedHashWithSalt(config, salt))[..12];
                _persistedFile = Path.Combine(persistedFolder, $"secret-cache-{name}.json.aes");

                // If either the salt of the config changes, we loose the cache, both the filename and password will fail
                using (var hasher = HashFactory.CreateHasher(HashFactory.SHA256))
                    _passphrase = Convert.ToBase64String($"{_salt}:{_config}".ComputeHash(hasher));
            }
            else
            {
                _persistedFile = null;
                _passphrase = null;
            }
        }

        /// <inheritdoc/>
        public string Key => _provider.Key;

        /// <inheritdoc/>
        public string DisplayName => _provider.DisplayName;

        /// <inheritdoc/>
        public string Description => _provider.Description;

        /// <inheritdoc/>
        public IList<ICommandLineArgument> SupportedCommands => _provider.SupportedCommands;

        /// <inheritdoc/>
        public async Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
        {
            try
            {
                // Always initialize the provider, and use this if possible
                await _provider.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
            catch
            {
                if (_cachingLevel == CachingLevel.None)
                    throw;
                if (_cachingLevel == CachingLevel.InMemory && !_cache.ContainsKey(_config))
                    throw;
                if (_cachingLevel == CachingLevel.Persistent)
                {
                    await LoadCacheAsync(cancellationToken).ConfigureAwait(false);
                    if (!_cache.ContainsKey(_config))
                        throw;
                }
            }
        }

        /// <summary>
        /// Loads the cache from disk, failing silently if the file could not be read
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task LoadCacheAsync(CancellationToken cancellationToken)
        {
            bool tryLoad;
            lock (_lock)
                tryLoad = _cachingLevel == CachingLevel.Persistent && !_cache.ContainsKey(_config) && File.Exists(_persistedFile);

            if (tryLoad && !string.IsNullOrEmpty(_passphrase))
            {
                // Load from disk
                try
                {
                    using (var fs = new FileStream(_persistedFile!, FileMode.Open, FileAccess.Read))
                    using (var ms = new MemoryStream())
                    {
                        var decOpts = SharpAESCrypt.DecryptionOptions.Default with { LeaveOpen = true };
                        await SharpAESCrypt.AESCrypt.DecryptAsync(_passphrase, fs, ms, decOpts, cancellationToken).ConfigureAwait(false);

                        ms.Position = 0;
                        var res = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ms, cancellationToken: cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Failed to deserialize the cache");

                        lock (_lock)
                            if (!_cache.ContainsKey(_config))
                                _cache[_config] = res;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "LoadPersistedCacheError", ex, "Failed to load cache from disk: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Saves the cache to disk
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task SaveCacheAsync(CancellationToken cancellationToken)
        {
            bool trySave;
            lock (_lock)
                trySave = _initialized && _cachingLevel == CachingLevel.Persistent && _cache.ContainsKey(_config);

            if (trySave && !string.IsNullOrEmpty(_passphrase))
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        Dictionary<string, string> data;
                        lock (_lock)
                            data = _cache[_config].ToDictionary(k => k.Key, k => k.Value);

                        await System.Text.Json.JsonSerializer.SerializeAsync(ms, data, cancellationToken: cancellationToken).ConfigureAwait(false);
                        ms.Position = 0;

                        var encOpts = SharpAESCrypt.EncryptionOptions.Default;
                        using (var fs = new FileStream(_persistedFile!, FileMode.Create, FileAccess.Write))
                            await SharpAESCrypt.AESCrypt.EncryptAsync(_passphrase, ms, fs, encOpts, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "SavePersistedCacheError", ex, "Failed to save cache to disk: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Gets the cached values for the given keys
        /// </summary>
        /// <param name="keys">The keys to get</param>
        /// <returns>The cached values or null if not found</returns>
        private Dictionary<string, string>? GetFromCache(IEnumerable<string> keys)
        {
            if (_cachingLevel == CachingLevel.InMemory || _cachingLevel == CachingLevel.Persistent)
            {
                lock (_lock)
                    if (_cache.ContainsKey(_config) && keys.All(x => _cache[_config].ContainsKey(x)))
                        return keys.ToDictionary(k => k, k => _cache[_config][k]);
            }

            return null;

        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            // Don't call the provider if it was not initialized
            if (!_initialized)
            {
                var cached = GetFromCache(keys);
                if (cached != null)
                    return cached;

                throw new InvalidOperationException("The provider has not been initialized");
            }

            // Always call the provider to get fresh values, if it was initialized
            Dictionary<string, string> result;
            try
            {
                result = await _provider.ResolveSecretsAsync(keys, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (_cachingLevel == CachingLevel.None)
                    throw;

                if (_cachingLevel == CachingLevel.InMemory || _cachingLevel == CachingLevel.Persistent)
                {
                    var cached = GetFromCache(keys);
                    if (cached != null)
                        return cached;
                }

                throw;
            }

            // We have a result, cache it
            if (_cachingLevel == CachingLevel.InMemory || _cachingLevel == CachingLevel.Persistent)
            {
                lock (_lock)
                {
                    if (!_cache.ContainsKey(_config))
                    {
                        _cache[_config] = result;
                    }
                    else
                    {
                        foreach (var k in result)
                            _cache[_config][k.Key] = k.Value;
                    }
                }

                if (_cachingLevel == CachingLevel.Persistent)
                    await SaveCacheAsync(cancellationToken).ConfigureAwait(false);
            }


            return result;
        }
    }
}