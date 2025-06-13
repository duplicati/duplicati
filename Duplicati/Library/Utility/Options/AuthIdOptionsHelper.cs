

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
using System;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility.Options;

/// <summary>
/// Helper class for unified use of authentication settings
/// </summary>
public static class AuthIdOptionsHelper
{
    /// <summary>
    /// The default URL to use for the OAuth login hosted on GAE.
    /// </summary>
    private const string DEFAULT_DUPLICATI_OAUTH_SERVICE = "https://duplicati-oauth-handler.appspot.com/refresh";
    /// <summary>
    /// The default URL to use for the new hosted OAuth login server.
    /// </summary>
    private const string DEFAULT_DUPLICATI_OAUTH_SERVICE_NEW = "https://oauth-service.duplicati.com/refresh";

    /// <summary>
    /// Helper method to get an environment variable with a default value.
    /// </summary>
    /// <param name="varName">The name of the environment variable to retrieve.</param>
    /// <param name="defaultValue">The default value to return if the environment variable is not set or is empty.</param>
    /// <returns>The value of the environment variable or the default value if it is not set.</returns>
    private static string GetEnvVar(string varName, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(varName);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>
    /// The URL to use for the OAuth service
    /// </summary>
    public static readonly string DUPLICATI_OAUTH_SERVICE =
        GetEnvVar("DUPLICATI_OAUTH_SERVICE", DEFAULT_DUPLICATI_OAUTH_SERVICE);

    /// <summary>
    /// The URL to use for the new OAuth service
    /// </summary>
    public static readonly string DUPLICATI_OAUTH_SERVICE_NEW =
        GetEnvVar("DUPLICATI_OAUTH_SERVICE", DEFAULT_DUPLICATI_OAUTH_SERVICE_NEW);

    /// <summary>
    /// Returns the URL to use for obtaining an OAuth token for the given module.
    /// </summary>
    /// <param name="modulename">The name of the module to use.</param>
    /// <returns>The URL to use for obtaining an OAuth token.</returns>
    public static string GetOAuthLoginUrl(string modulename, string? oauthurl)
    {
        if (string.IsNullOrWhiteSpace(oauthurl))
            oauthurl = DUPLICATI_OAUTH_SERVICE;
        var u = new Uri(oauthurl);
        var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
        return string.Format(addr.ToString(), modulename);
    }

    /// <summary>
    /// Returns the URL to use for obtaining an OAuth token for the given module, defaulting to the new server.
    /// </summary>
    /// <param name="modulename">The name of the module to use.</param>
    public static string GetOAuthLoginUrlNew(string modulename, string? oauthurl)
    {
        if (string.IsNullOrWhiteSpace(oauthurl))
            oauthurl = DUPLICATI_OAUTH_SERVICE_NEW;

        var u = new Uri(oauthurl);
        var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
        return string.Format(addr.ToString(), modulename);
    }

    /// <summary>
    /// The authentication ID option, without a prefix
    /// </summary>
    public const string AuthIdOption = "authid";
    /// <summary>
    /// The OAuth server URL option, without a prefix
    /// </summary>
    public const string OAuthUrlOption = "oauth-url";
    /// <summary>
    /// Gets the authentication options
    /// </summary>
    /// <param name="tokenurl">The URL to use for the token</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The authentication options</returns>
    public static CommandLineArgument[] GetOptions(string tokenurl, string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{AuthIdOption}", CommandLineArgument.ArgumentType.Password, Strings.AuthIdSettingsHelper.AuthidShort, Strings.AuthIdSettingsHelper.AuthidLong(tokenurl)),
        .. GetServerOnlyOptions(prefix)
    ];

    /// <summary>
    /// Gets the server-only authentication options
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The server-only authentication options</returns>
    public static CommandLineArgument[] GetServerOnlyOptions(string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{OAuthUrlOption}", CommandLineArgument.ArgumentType.String, Strings.AuthIdSettingsHelper.OauthurlShort, Strings.AuthIdSettingsHelper.OauthurlLong)
    ];

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthIdOptions Parse(IReadOnlyDictionary<string, string?> options, string? prefix = null)
    {
        var oauthUrl = options.GetValueOrDefault($"{prefix}{OAuthUrlOption}");
        if (string.IsNullOrEmpty(oauthUrl))
            oauthUrl = DUPLICATI_OAUTH_SERVICE;

        return new AuthIdOptions(options.GetValueOrDefault($"{prefix}{AuthIdOption}"), oauthUrl);
    }

    /// <summary>
    /// Structure to hold the authentication options
    /// </summary>
    /// <param name="AuthId">The Auth ID</param>
    public sealed record AuthIdOptions(string? AuthId, string OAuthUrl)
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
