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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

public class SecretProviderHelperTests : BasicSetupHelper
{
    private class MockedSecretProvider : ISecretProvider
    {
        public string Key => "mock";

        public string DisplayName => "";

        public string Description => "";

        public Dictionary<string, string> Secrets { get; set; } = new();

        public IList<ICommandLineArgument> SupportedCommands => [];

        public bool ThrowOnInit { get; set; }

        public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
        {
            if (ThrowOnInit)
                throw new InvalidOperationException("Initialization failed");

            return Task.CompletedTask;
        }

        public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keys)
            {
                if (!Secrets.TryGetValue(key, out var value))
                    throw new KeyNotFoundException($"The key '{key}' was not found");

                result[key] = value;
            }

            return Task.FromResult(result);
        }
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceSecretsWithDefaultSettings()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", "$key2"},
            {"key3", "value3$key"},
        };

        var argsSys = new[] {
            new System.Uri("test://host/?pass=$key2&user=$key1&other=123")
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=$key2&user=$key1&other=123")
        };

        SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await();

        Assert.AreEqual("value1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
        Assert.AreEqual("value3$key", settings["key3"]);
        Assert.AreEqual("test://host/?pass=secret2&user=secret1&other=123", argsSys[0].ToString());
        Assert.AreEqual("test://host?pass=secret2&user=secret1&other=123", argsInternal[0].ToString());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceSecretsWithSlashKeys()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key/with/slash"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", "$key/with/slash"},
            {"key3", "value3$key"},
        };

        var argsSys = new[] {
            new System.Uri("test://host/?pass=$key/with/slash&user=$key1&other=123")
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=$key/with/slash&user=$key1&other=123")
        };

        SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await();

        Assert.AreEqual("value1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
        Assert.AreEqual("value3$key", settings["key3"]);
        Assert.AreEqual("test://host/?pass=secret2&user=secret1&other=123", argsSys[0].ToString());
        Assert.AreEqual("test://host?pass=secret2&user=secret1&other=123", argsInternal[0].ToString());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplacesSecretsWithUrlEscaping()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret 1%&+abc";

        var settings = new Dictionary<string, string>
        {
            {"key1", "$key1"}
        };

        var argsSys = new[] {
            new System.Uri("test://host/?pass=$key1&user=user")
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=$key1&user=user")
        };

        SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await();

        // Check no escaping is done for options
        Assert.AreEqual("secret 1%&+abc", settings["key1"]);

        // Check escaping is done for the URL
        Assert.AreEqual("test://host/?pass=secret+1%25%26%2Babc&user=user".ToLowerInvariant(), argsSys[0].ToString().ToLowerInvariant());
        Assert.AreEqual("test://host?pass=secret+1%25%26%2Babc&user=user", argsInternal[0].ToString());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceSecretsWithExtendedPattern()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", "${key2}"},
            {"key3", "value3${key}"},
            {"secret-provider-pattern", "${}"},
        };

        var argsSys = new[] {
            new System.Uri("test://host/?pass=${key2}&user=${key1}&other=123"),
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=${key2}&user=${key1}&other=123"),
        };

        SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await();

        Assert.AreEqual("value1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
        Assert.AreEqual("value3${key}", settings["key3"]);
        Assert.AreEqual("test://host/?pass=secret2&user=secret1&other=123", argsSys[0].ToString());
        Assert.AreEqual("test://host?pass=secret2&user=secret1&other=123", argsInternal[0].ToString());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceSecretsWithExtendedLongPattern()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", ":sec{key2}"},
            {"key3", "value3:sec{key}"},
            {"secret-provider-pattern", ":sec{}"},
        };

        var argsSys = new[] {
            new System.Uri("test://host/?pass=:sec{key2}&user=:sec{key1}&other=123"),
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=:sec{key2}&user=:sec{key1}&other=123"),
        };

        SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await();

        Assert.AreEqual("value1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
        Assert.AreEqual("value3:sec{key}", settings["key3"]);
        Assert.AreEqual("test://host/?pass=secret2&user=secret1&other=123", argsSys[0].ToString());
        Assert.AreEqual("test://host?pass=secret2&user=secret1&other=123", argsInternal[0].ToString());
    }

    [Test]
    [Category("SecretHelper")]
    public void CachedProviderRetainsPattern()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["sec1"] = "secret1";

        var settings = new Dictionary<string, string>
        {
            {"key1", "!ext{sec1}"}
        };

        var cachedProvider = SecretProviderHelper.WrapWithCache("", secretProvider, SecretProviderHelper.CachingLevel.InMemory, null, "salt", "!ext{}");
        cachedProvider.InitializeAsync(new System.Uri("mock://"), CancellationToken.None).Await();

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        Assert.AreEqual("secret1", settings["key1"]);

        settings["key1"] = "!ext{sec1}";

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        Assert.AreEqual("secret1", settings["key1"]);
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceFailsIfKeyIsMissing()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", "$key2"},
        };

        var argsSys = new[] {
            new System.Uri("test://host?pass=$key2&user=$key1&other=123"),
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=$key2&user=$key1&other=123"),
        };

        Assert.Throws<KeyNotFoundException>(() => SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceFailsIfValueIsEmpty()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "";

        var settings = new Dictionary<string, string>
        {
            {"key1", "value1"},
            {"key2", "$key2"},
        };

        var argsSys = new[] {
            new System.Uri("test://host?pass=$key2&user=$key1&other=123"),
        };

        var argsInternal = new[] {
            new Library.Utility.Uri("test://host?pass=$key2&user=$key1&other=123"),
        };

        Assert.Throws<InvalidOperationException>(() => SecretProviderHelper.ApplySecretProviderAsync(argsSys, argsInternal, settings, null, secretProvider, CancellationToken.None).Await());
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceUsesInMemoryCache()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "$key1"},
            {"key2", "$key2"}
        };

        var cachedProvider = SecretProviderHelper.WrapWithCache("", secretProvider, SecretProviderHelper.CachingLevel.InMemory, null, "salt", null);
        cachedProvider.InitializeAsync(new System.Uri("mock://"), CancellationToken.None).Await();

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        secretProvider.Secrets = null;

        settings["key1"] = "$key1";
        settings["key2"] = "$key2";

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        Assert.AreEqual("secret1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceUsesPersistedCache()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "$key1"},
            {"key2", "$key2"}
        };

        using var tempFolder = new TempFolder();
        var cachedProvider = SecretProviderHelper.WrapWithCache("", secretProvider, SecretProviderHelper.CachingLevel.Persistent, tempFolder, "salt", null);
        cachedProvider.InitializeAsync(new System.Uri("mock://"), CancellationToken.None).Await();

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        secretProvider.Secrets = null;

        settings["key1"] = "$key1";
        settings["key2"] = "$key2";

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        Assert.AreEqual("secret1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
    }

    [Test]
    [Category("SecretHelper")]
    public void ReplaceUsesPersistedCacheOnRestart()
    {
        var secretProvider = new MockedSecretProvider();
        secretProvider.Secrets["key1"] = "secret1";
        secretProvider.Secrets["key2"] = "secret2";

        var settings = new Dictionary<string, string>
        {
            {"key1", "$key1"},
            {"key2", "$key2"}
        };

        using var tempFolder = new TempFolder();
        var cachedProvider = SecretProviderHelper.WrapWithCache("", secretProvider, SecretProviderHelper.CachingLevel.Persistent, tempFolder, "salt", null);
        cachedProvider.InitializeAsync(new System.Uri("mock://"), CancellationToken.None).Await();

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        settings["key1"] = "$key1";
        settings["key2"] = "$key2";

        // Create a new instance of the provider that is unavailable
        cachedProvider = SecretProviderHelper.WrapWithCache("", secretProvider, SecretProviderHelper.CachingLevel.Persistent, tempFolder, "salt", null);
        secretProvider.Secrets = null;
        secretProvider.ThrowOnInit = true;

        cachedProvider.InitializeAsync(new System.Uri("mock://"), CancellationToken.None).Await();

        SecretProviderHelper.ApplySecretProviderAsync([], [], settings, null, cachedProvider, CancellationToken.None).Await();

        Assert.AreEqual("secret1", settings["key1"]);
        Assert.AreEqual("secret2", settings["key2"]);
    }

}
