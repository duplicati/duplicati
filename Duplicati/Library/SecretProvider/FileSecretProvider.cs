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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <inheritdoc />
    public string Key => "file-secret";

    /// <inheritdoc />
    public string DisplayName => Strings.FileSecretProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.FileSecretProvider.Description;

    /// <inheritdoc />
    public bool IsSupported() => true;

    /// <inheritdoc />
    public bool IsSetSupported => !string.IsNullOrWhiteSpace(_passphrase);

    /// <inheritdoc />
    private Dictionary<string, string>? _secrets;

    /// <summary>
    /// The file path to read/write secrets from
    /// </summary>
    private string? _filePath;
    /// <summary>
    /// The passphrase to use for encrypting/decrypting the file
    /// </summary>
    private string? _passphrase;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [
        new CommandLineArgument(PASSPHRASE_OPTION, CommandLineArgument.ArgumentType.String, Strings.FileSecretProvider.PassphraseDescriptionShort, Strings.FileSecretProvider.PassphraseDescriptionLong, null),
    ];

    /// <inheritdoc />
    public async Task InitializeAsync(Uri config, CancellationToken cancellationToken)
    {
        _filePath = config.LocalPath;
        if (_filePath is null || !File.Exists(_filePath))
            throw new FileNotFoundException($"File not found: {_filePath}");

        // Get the passphrase from the secrets-passphrase query parameter
        var args = HttpUtility.ParseQueryString(config.Query);
        _passphrase = args[PASSPHRASE_OPTION];
        using var fs = File.OpenRead(_filePath);
        Dictionary<string, string> secrets;

        if (string.IsNullOrEmpty(_passphrase))
        {
            secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fs, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new UserInformationException("The file does not contain any secrets", "NoSecrets");
        }
        else
        {
            using var ms = new MemoryStream();
            await SharpAESCrypt.AESCrypt.DecryptAsync(_passphrase, fs, ms, SharpAESCrypt.DecryptionOptions.Default with { LeaveOpen = true }, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ms, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new UserInformationException("The file does not contain any secrets", "NoSecrets");
        }

        // Make secret keys case-insensitive
        _secrets = secrets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_secrets is null)
            throw new InvalidOperationException("The secret provider has not been initialized");

        return Task.FromResult(keys.ToDictionary(k => k, k => _secrets.TryGetValue(k, out var value) ? value : throw new KeyNotFoundException($"The key '{k}' was not found")));
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken)
    {
        if (_secrets is null || string.IsNullOrEmpty(_filePath))
            throw new InvalidOperationException("The secret provider has not been initialized");

        if (string.IsNullOrEmpty(_passphrase))
            throw new InvalidOperationException("The secret provider does not support setting secrets without a passphrase");

        if (!overwrite && _secrets.ContainsKey(key))
            throw new InvalidOperationException($"The key '{key}' already exists");

        _secrets[key] = value;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (string.IsNullOrEmpty(_passphrase))
        {
            await using var fs = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(fs, _secrets, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var jsonStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(jsonStream, _secrets, SerializerOptions, cancellationToken).ConfigureAwait(false);
            jsonStream.Position = 0;

            await using var fs = File.Create(_filePath);
            await SharpAESCrypt.AESCrypt.EncryptAsync(_passphrase, jsonStream, fs, SharpAESCrypt.EncryptionOptions.Default with { LeaveOpen = true }, cancellationToken).ConfigureAwait(false);
        }
    }
}
