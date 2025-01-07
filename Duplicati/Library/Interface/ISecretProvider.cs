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
public interface ISecretProvider : IDynamicModule
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
    /// The key for the secret provider
    /// </summary>
    string Key { get; }
    /// <summary>
    /// The display name of the secret provider
    /// </summary>
    string DisplayName { get; }
    /// <summary>
    /// The description of the secret provider
    /// </summary>
    string Description { get; }
}
