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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.SecretProvider;

namespace Duplicati.Library.DynamicLoader;

public class SecretProviderLoader
{
    /// <summary>
    /// Loader for secret providers
    /// </summary>
    private class SecretProviderLoaderSub : DynamicLoader<ISecretProvider>
    {
        /// <summary>
        /// Gets the key for the secret provider
        /// </summary>
        /// <param name="item">The item to get the key for</param>
        /// <returns>The key</returns>
        protected override string GetInterfaceKey(ISecretProvider item)
            => item.Key;

        /// <summary>
        /// Returns the subfolders searched for secret providers
        /// </summary>
        protected override string[] Subfolders => ["secretproviders"];

        /// <summary>
        /// The built-in modules
        /// </summary>
        protected override IEnumerable<ISecretProvider> BuiltInModules => SecretProviderModules.Modules;

        /// <summary>
        /// Gets the supported commands for a certain key
        /// </summary>
        /// <param name="key">The key to find commands for</param>
        /// <returns>The supported commands or null if the key was not found</returns>
        public IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            LoadInterfaces();

            lock (m_lock)
            {
                if (m_interfaces.TryGetValue(key, out var b) && b != null)
                    return GetSupportedCommandsCached(b).ToList();
                else
                    return [];
            }
        }
    }

    /// <summary>
    /// The loader instance
    /// </summary>
    private static readonly Lazy<SecretProviderLoaderSub> _loader = new(() => new SecretProviderLoaderSub());
    /// <summary>
    /// The secret provider modules
    /// </summary>
    public static ISecretProvider[] Modules => _loader.Value.Interfaces;
    /// <summary>
    /// The keys for the secret providers
    /// </summary>
    public static string[] Keys { get { return _loader.Value.Keys; } }

    /// <summary>
    /// Gets the supported commands for a certain key
    /// </summary>
    /// <param name="key">The key to find commands for</param>
    /// <returns>The supported commands or null if the key was not found</returns>
    public static IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string key)
        => _loader.Value.GetSupportedCommands(key);

    /// <summary>
    /// Returns the metadata for a provider
    /// </summary>
    /// <param name="key">The key to get metadata for</param>
    /// <returns>The key, description, and supported commands</returns>
    public static (string Key, string DisplayName, string Description, IReadOnlyList<ICommandLineArgument> SupportedCommands) GetProviderMetadata(string key)
    {
        var provider = _loader.Value.Interfaces.FirstOrDefault(p => p.Key == key);
        if (provider == null)
            throw new ArgumentException($"No secret provider found for key {key}");

        return (provider.Key, provider.DisplayName, provider.Description, provider.SupportedCommands.AsReadOnly());
    }

    /// <summary>
    /// Creates an instance of a secret provider
    /// </summary>
    /// <param name="config">The configuration string</param>
    /// <returns>The secret provider instance</returns>
    public static ISecretProvider CreateInstance(string config)
    {
        if (string.IsNullOrEmpty(config))
            throw new ArgumentNullException(nameof(config));

        // Translate from environment variables
        string? envName = null;
        if (config.StartsWith("$"))
        {
            envName = config[1..];
            if (envName.StartsWith("{") && envName.EndsWith("}"))
                envName = envName[1..^1];
        }
        else if (config.StartsWith("%") && config.EndsWith("%"))
        {
            envName = config[1..^1];
        }

        if (envName != null)
        {
            var result = Environment.GetEnvironmentVariable(envName.ToUpperInvariant());
            if (string.IsNullOrEmpty(result))
                throw new ArgumentException($"The environment variable {envName} was not found");

            config = result;
        }

        var uri = new Uri(config);
        var key = uri.Scheme;

        var providerType = Modules.FirstOrDefault(p => p.Key == key)
            ?? throw new ArgumentException($"No secret provider found for key {key}");

        if (Activator.CreateInstance(providerType.GetType()) is not ISecretProvider provider)
            throw new InvalidOperationException($"Failed to create an instance of {providerType}");

        return provider;
    }
}
