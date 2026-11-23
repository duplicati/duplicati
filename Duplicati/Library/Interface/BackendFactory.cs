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

using System;
using System.Collections.Generic;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Creates a backend from a url, for code that cannot reference the backend loader
    /// because the loader references it, such as a backend that wraps another backend.
    /// The backend loader registers itself here when it is first used.
    /// </summary>
    public static class BackendFactory
    {
        /// <summary>
        /// The registered factory, or <c>null</c> before the backend loader has been used
        /// </summary>
        private static volatile Func<string, Dictionary<string, string?>, IBackend?>? _factory;

        /// <summary>
        /// Registers the method that creates a backend from a url and options
        /// </summary>
        /// <param name="factory">The method; it returns <c>null</c> for a url no backend supports</param>
        public static void Register(Func<string, Dictionary<string, string?>, IBackend?> factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        /// <summary>
        /// Creates the backend for a url
        /// </summary>
        /// <param name="url">The backend url; its query parameters are added to the options</param>
        /// <param name="options">The options to create the backend with</param>
        /// <returns>The backend, or <c>null</c> if no backend supports the url</returns>
        /// <exception cref="InvalidOperationException">No factory has been registered</exception>
        public static IBackend? Create(string url, Dictionary<string, string?> options)
            => (_factory ?? throw new InvalidOperationException("No backend factory is registered; the backend loader has not been initialized"))
                (url, options);
    }
}
