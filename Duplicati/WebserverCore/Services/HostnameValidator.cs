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
using System.Net;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Implementation of hostname validation
/// </summary>
public class HostnameValidator : IHostnameValidator
{
    /// <summary>
    /// Hostnames that are always allowed
    /// </summary>
    private static readonly string[] DefaultAllowedHostnames = ["localhost", "127.0.0.1", "[::1]", "localhost.localdomain"];
    /// <summary>
    /// The list of allowed hostnames
    /// </summary>
    private readonly HashSet<string> m_allowedHostnames;
    /// <summary>
    /// A flag that indicates if any hostname is allowed
    /// </summary>
    private readonly bool m_allowAny;

    /// <summary>
    /// Creates a new instance of the <see cref="HostnameValidator"/> class
    /// </summary>
    /// <param name="allowedHostnames">The list of allowed hostnames</param>
    public HostnameValidator(IEnumerable<string> allowedHostnames)
    {
        m_allowedHostnames = (allowedHostnames ?? []).Concat(DefaultAllowedHostnames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        m_allowAny = m_allowedHostnames.Contains("*");
    }

    /// <inheritdoc />
    public bool IsValidHostname(string hostname)
    {
        if (m_allowAny)
            return true;

        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        if (m_allowedHostnames.Contains(hostname))
            return true;

        if (IPAddress.TryParse(hostname, out _))
            return true;

        return false;
    }
}
