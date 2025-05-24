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
using Duplicati.Library.Utility;

#nullable enable    
namespace Duplicati.Library;

/// <summary>
/// Class for providing call-context access to http settings
/// </summary>
public static class OAuthContextSettings
{
    /// <summary>
    /// The URL to use for the OAuth login hosted on GAE.
    /// </summary>
    public const string DUPLICATI_OAUTH_SERVICE = "https://duplicati-oauth-handler.appspot.com/refresh";
    /// <summary>
    /// The URL to use for the new hosted OAuth login server.
    /// </summary>
    public const string DUPLICATI_OAUTH_SERVICE_NEW = "https://oauth-service.duplicati.com/refresh";

    /// <summary>
    /// The struct wrapping the OAuth settings
    /// </summary>
    private struct OAuthSettings
    {
        /// <summary>
        /// The server url
        /// </summary>
        public string ServerURL;
    }

    /// <summary>
    /// Starts the session.
    /// </summary>
    /// <returns>The session.</returns>
    /// <param name="serverurl">The url to use for the server.</param>
    public static IDisposable StartSession(string serverurl)
    {
        return CallContextSettings<OAuthSettings>.StartContext(new OAuthSettings { ServerURL = serverurl });
    }

    /// <summary>
    /// Gets the server URL to use for OAuth.
    /// </summary>
    public static string ServerURL
    {
        get
        {
            var r = CallContextSettings<OAuthSettings>.Settings.ServerURL;
            return string.IsNullOrWhiteSpace(r) ? DUPLICATI_OAUTH_SERVICE : r;
        }
    }

    /// <summary>
    /// Gets the server URL to use for OAuth, without applying a default.
    /// </summary>
    public static string? ServerURLRaw => CallContextSettings<OAuthSettings>.Settings.ServerURL;
}
