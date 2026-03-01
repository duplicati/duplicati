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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Certificates;

/// <summary>
/// Detects local hostnames and IP addresses for SSL certificate SANs.
/// </summary>
public static class HostnameDetector
{
    /// <summary>
    /// The list of always-included hostnames (localhost and loopback addresses).
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysIncludedHostnames = new[]
    {
        "localhost",
        "127.0.0.1",
        "::1"
    };

    /// <summary>
    /// Detects all local hostnames and IP addresses that should be included in the server certificate.
    /// Always includes localhost, 127.0.0.1, and ::1.
    /// </summary>
    /// <returns>A list of hostnames and IP addresses.</returns>
    public static List<string> DetectHostnames()
    {
        var hostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include localhost and loopback addresses
        hostnames.UnionWith(AlwaysIncludedHostnames);

        // Add the machine name
        try
        {
            var machineName = Environment.MachineName;
            if (IsValidDnsName(machineName))
                hostnames.Add(machineName);
        }
        catch
        {
            // Ignore errors getting machine name
        }

        // Add the domain-qualified machine name if available
        try
        {
            var domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (IsValidDnsName(domainName))
            {
                var machineName = Environment.MachineName;
                if (IsValidDnsName(machineName))
                    hostnames.Add($"{machineName}.{domainName}");
            }
        }
        catch
        {
            // Ignore errors getting domain name
        }

        // Explictly do not add any external IP addresses, as we expect a localhost only binding
        // Additional hostnames can be added by the user if needed

        return hostnames.ToList();
    }

    /// <summary>
    /// Validates and normalizes a list of hostnames provided by the user.
    /// </summary>
    /// <param name="hostnames">The hostnames to validate.</param>
    /// <returns>A normalized list of valid hostnames.</returns>
    public static List<string> ValidateHostnames(IEnumerable<string> hostnames)
    {
        var validHostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hostname in hostnames)
        {
            var trimmed = hostname.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Check if it's a valid IP address
            if (IPAddress.TryParse(trimmed, out var ipAddress))
            {
                // Only support IPv4 and IPv6
                if (ipAddress.AddressFamily == AddressFamily.InterNetwork ||
                    ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    validHostnames.Add(trimmed);
                }
                continue;
            }

            // Check if it's a valid DNS name
            // Basic validation: must not contain spaces or invalid characters
            if (IsValidDnsName(trimmed))
                validHostnames.Add(trimmed.ToLowerInvariant());
        }

        return validHostnames.ToList();
    }

    /// <summary>
    /// Regex pattern for validating DNS hostnames per RFC 1123.
    /// </summary>
    /// <remarks>
    /// Pattern breakdown:
    /// -^(?=.{1,253}$) - Ensure total length is 1-253 characters
    /// - [a-zA-Z0-9] - Label must start with alphanumeric
    /// - ([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])? - Optional middle part (0-61 chars) ending with alphanumeric
    /// - (\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)* - Additional labels separated by dots
    /// </remarks>
    private static readonly Regex DnsNameRegex = new(
        @"^(?=.{1,253}$)[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Performs basic validation of a DNS name using regex.
    /// </summary>
    /// <param name="name">The DNS name to validate.</param>
    /// <returns>True if the name appears to be valid.</returns>
    private static bool IsValidDnsName(string name)
        => !string.IsNullOrWhiteSpace(name) && DnsNameRegex.IsMatch(name);
}
