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

using Duplicati.Library.Interface;
using Meziantou.Framework.Win32;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Secret provider that reads secrets from Windows Credential Manager
/// </summary>
public class WindowsCredentialManagerProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "wincred";

    /// <inheritdoc />
    public string DisplayName => Strings.WindowsCredentialManagerProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.WindowsCredentialManagerProvider.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [];

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows");
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows XP or later");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            throw new PlatformNotSupportedException("Windows Credential Manager is only supported on Windows");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var cred = CredentialManager.ReadCredential(key);
            var value = cred?.Password;
            if (string.IsNullOrWhiteSpace(value))
                throw new KeyNotFoundException($"The key '{key}' was not found");

            result[key] = value;
        }

        return Task.FromResult(result);
    }
}
