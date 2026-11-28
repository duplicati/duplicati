// Copyright (C) 2026, The Duplicati Team
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

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// A backend that wraps another backend, for example to translate the remote names.
/// A wrapper implements every optional backend interface and passes the calls on,
/// so a check with <c>is</c> cannot tell what the wrapped backend supports.
/// Use <see cref="BackendCapabilityExtensions.GetCapabilityAsync{T}"/> instead.
/// </summary>
public interface IWrappingBackend : IBackend
{
    /// <summary>
    /// The backend that this backend wraps
    /// </summary>
    IBackend WrappedBackend { get; }
}

/// <summary>
/// A backend that implements optional backend interfaces it cannot always serve, for example
/// because the storage it ends up using is only known after it has connected.
/// <see cref="BackendCapabilityExtensions.GetCapabilityAsync{T}"/> asks it before trusting
/// the interfaces it implements.
/// </summary>
public interface IConditionalCapabilityBackend : IBackend
{
    /// <summary>
    /// Gets a value indicating whether the backend can serve an optional backend interface now
    /// </summary>
    /// <typeparam name="T">The optional backend interface</typeparam>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns><c>true</c> if calls through the interface are supported</returns>
    Task<bool> HasCapabilityAsync<T>(CancellationToken cancellationToken) where T : class, IBackend;
}

/// <summary>
/// Extensions for finding what a backend supports
/// </summary>
public static class BackendCapabilityExtensions
{
    /// <summary>
    /// Gets the backend as an optional backend interface, if it supports it.
    /// A wrapping backend supports the interface when the backend it wraps does, and a
    /// <see cref="IConditionalCapabilityBackend"/> anywhere in the chain can decline it.
    /// </summary>
    /// <typeparam name="T">The optional backend interface</typeparam>
    /// <param name="backend">The backend to check</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The backend as the interface, or <c>null</c> if it does not support it</returns>
    public static async Task<T?> GetCapabilityAsync<T>(this IBackend? backend, CancellationToken cancellationToken)
        where T : class, IBackend
    {
        var inner = backend;
        while (true)
        {
            if (inner is IConditionalCapabilityBackend conditional && !await conditional.HasCapabilityAsync<T>(cancellationToken).ConfigureAwait(false))
                return null;
            if (inner is not IWrappingBackend wrapping)
                break;
            inner = wrapping.WrappedBackend;
        }

        return inner is T ? backend as T : null;
    }

    /// <summary>
    /// Gets a value indicating whether the backend supports an optional backend interface.
    /// A wrapping backend supports the interface when the backend it wraps does.
    /// </summary>
    /// <typeparam name="T">The optional backend interface</typeparam>
    /// <param name="backend">The backend to check</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns><c>true</c> if the backend supports the interface</returns>
    public static async Task<bool> SupportsAsync<T>(this IBackend? backend, CancellationToken cancellationToken)
        where T : class, IBackend
        => await backend.GetCapabilityAsync<T>(cancellationToken).ConfigureAwait(false) != null;
}
