

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
public static class AuthOptionsHelper
{
    /// <summary>
    /// Gets the authentication options
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The authentication options</returns>
    public static CommandLineArgument[] GetOptions(string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}auth-username", CommandLineArgument.ArgumentType.String, Strings.AuthSettingsHelper.DescriptionAuthUsernameShort, Strings.AuthSettingsHelper.DescriptionAuthUsernameLong),
        new CommandLineArgument($"{prefix}auth-password", CommandLineArgument.ArgumentType.Password, Strings.AuthSettingsHelper.DescriptionAuthPasswordShort, Strings.AuthSettingsHelper.DescriptionAuthPasswordLong)
    ];

    /// <summary>
    /// Gets the authentication options
    /// </summary>
    /// <param name="username">The name of the username options</param>
    /// <param name="password">The name of the password options</param>
    /// <returns>The authentication options</returns>
    public static CommandLineArgument[] GetOptionsWithAlias(string username, string password) =>
    [
        new CommandLineArgument(username, CommandLineArgument.ArgumentType.String, Strings.AuthSettingsHelper.DescriptionAuthUsernameShort, Strings.AuthSettingsHelper.DescriptionAuthUsernameLong, null, ["auth-username"]),
        new CommandLineArgument(password, CommandLineArgument.ArgumentType.Password, Strings.AuthSettingsHelper.DescriptionAuthPasswordShort, Strings.AuthSettingsHelper.DescriptionAuthPasswordLong, null, ["auth-password"])
    ];

    /*

    TODO: See if we can do without these, so we only have one way of parsing the auth options

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthOptions Parse(IReadOnlyDictionary<string, string?> options, string? prefix = null)
        => new AuthOptions(
            options.GetValueOrDefault($"{prefix}auth-username"),
            options.GetValueOrDefault($"{prefix}auth-password")
        );

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="username">The name of the username options</param>
    /// <param name="password">The name of the password options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthOptions ParseWithAlias(IReadOnlyDictionary<string, string?> options, string username, string password)
    {
        var optionUsername = options.GetValueOrDefault(username);
        var optionPassword = options.GetValueOrDefault(password);

        if (string.IsNullOrWhiteSpace(optionUsername))
            optionUsername = options.GetValueOrDefault("auth-username");
        if (string.IsNullOrWhiteSpace(optionPassword))
            optionPassword = options.GetValueOrDefault("auth-password");

        return new AuthOptions(optionUsername, optionPassword);
    }
    */

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="uri">The URI to get the default values from</param>
    /// <param name="username">The name of the username options</param>
    /// <param name="password">The name of the password options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthOptions ParseWithAlias(IReadOnlyDictionary<string, string?> options, Uri uri, string username, string password)
    {
        // Prefer the primary name, if set
        var optionUsername = options.GetValueOrDefault(username);
        var optionPassword = options.GetValueOrDefault(password);
        var parsedOptions = Parse(options, uri);

        if (string.IsNullOrWhiteSpace(optionUsername))
            optionUsername = parsedOptions.Username;
        if (string.IsNullOrWhiteSpace(optionPassword))
            optionPassword = parsedOptions.Password;

        return new AuthOptions(optionUsername, optionPassword);
    }

    /// <summary>
    /// Parses the authentication options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="uri">The URI to get the default values from</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The parsed authentication options</returns>
    public static AuthOptions Parse(IReadOnlyDictionary<string, string?> options, Uri uri, string? prefix = null)
    {
        var optionUsername = options.GetValueOrDefault($"{prefix}auth-username");
        var optionPassword = options.GetValueOrDefault($"{prefix}auth-password");

        // Prefer the URL values, if set
        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.Username))
        {
            username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                password = uri.Password;
            else if (!string.IsNullOrWhiteSpace(optionPassword))
                password = optionPassword;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(optionUsername))
            {
                username = optionUsername;
                if (!string.IsNullOrWhiteSpace(optionPassword))
                    password = optionPassword;
            }
        }

        return new AuthOptions(username, password);
    }

    /// <summary>
    /// Structure to hold the authentication options
    /// </summary>
    /// <param name="Username">The username</param>
    /// <param name="Password">The password</param>
    public sealed record AuthOptions(string? Username, string? Password)
    {
        /// <summary>
        /// Checks if the username has a value
        /// </summary>
        public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
        /// <summary>
        /// Checks if the password has a value
        /// </summary>
        public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

        /// <summary>
        /// Checks if the username and password are set
        /// </summary>
        /// <returns>True if the username and password are set</returns>
        public bool IsValid() => HasUsername && HasPassword;

        /// <summary>
        /// Throws an exception if the username and password are not set
        /// </summary>
        public void RequireCredentials()
        {
            if (!IsValid())
                throw new UserInformationException(Strings.AuthSettingsHelper.UsernameAndPasswordRequired, "UsernameAndPasswordRequired");
        }
    }

}
