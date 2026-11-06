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
using Duplicati.WebserverCore.Middlewares;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Provides the active JWT configuration, which can be replaced at runtime
/// </summary>
public interface IJWTConfigProvider
{
    /// <summary>
    /// The JWT configuration currently used to issue and validate tokens
    /// </summary>
    JWTConfig Current { get; }

    /// <summary>
    /// Replaces the JWT configuration with a new signing key and removes all token families,
    /// invalidating every token issued so far
    /// </summary>
    void Reset();

    /// <summary>
    /// Raised after <see cref="Reset"/> has replaced the configuration
    /// </summary>
    event EventHandler? ConfigReset;
}
