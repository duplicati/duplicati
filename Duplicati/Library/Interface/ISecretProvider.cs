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

namespace Duplicati.Library.Interface;

/// <summary>
/// Interface for secret providers
/// </summary>
public interface ISecretProvider : ICommonModule
{
    // abstract static Task<T> CreateAsync<T>(string config, CancellationToken cancellationToken)
    //     where T : ISecretProvider;

    /// <summary>
    /// Initializes the secret provider
    /// </summary>
    /// <param name="config">The configuration string</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task InitializeAsync(Uri config, CancellationToken cancellationToken);
    /// <summary>
    /// Resolves the secrets for the given keys
    /// </summary>
    /// <param name="keys">The keys to resolve</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A dictionary of resolved secrets. The dictionary has all requested keys or the call fails.</returns>
    Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken);
    /// <summary>
    /// Stores a secret value.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="overwrite">If set to <c>true</c>, existing secrets are overwritten.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken);
    /// <summary>
    /// Indicates whether the secret provider is supported on the current platform.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<bool> IsSupported(CancellationToken cancellationToken);
    /// <summary>
    /// Indicates whether the secret provider supports setting secrets.
    /// </summary>
    bool IsSetSupported { get; }
}
