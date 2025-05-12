

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

#nullable enable
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility.Options;

/// <summary>
/// Helper class for unified use of authentication settings
/// </summary>
public static class AuthIdOptionsHelper
{
    /// <summary>
    /// The authentication ID option, without a prefix
    /// </summary>
    public const string AuthIdOption = "authid";
    /// <summary>
    /// Gets the authentication options
    /// </summary>
    /// <param name="tokenurl">The URL to use for the token</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The authentication options</returns>
    public static CommandLineArgument[] GetOptions(string tokenurl, string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{AuthIdOption}", CommandLineArgument.ArgumentType.Password, Strings.AuthIdSettingsHelper.AuthidShort, Strings.AuthIdSettingsHelper.AuthidLong(tokenurl))
    ];

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthIdOptions Parse(IReadOnlyDictionary<string, string?> options, string? prefix = null)
        => new AuthIdOptions(options.GetValueOrDefault($"{prefix}{AuthIdOption}"));

    /// <summary>
    /// Structure to hold the authentication options
    /// </summary>
    /// <param name="AuthId">The Auth ID</param>
    public sealed record AuthIdOptions(string? AuthId)
    {
        /// <summary>
        /// Checks if the authid is set
        /// </summary>
        public bool IsValid() => !string.IsNullOrEmpty(AuthId);

        /// <summary>
        /// Throws an exception if the username and password are not set
        /// </summary>
        /// <param name="tokenurl">The URL to use for the token</param>
        public AuthIdOptions RequireCredentials(string tokenurl)
        {
            if (!IsValid())
                throw new UserInformationException(Strings.AuthIdSettingsHelper.MissingAuthID(tokenurl), "MissingAuthID");
            return this;
        }
    }

}
