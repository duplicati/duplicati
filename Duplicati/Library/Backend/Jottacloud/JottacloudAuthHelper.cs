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

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Newtonsoft.Json;

namespace Duplicati.Library.Backend;

public class JottacloudAuthHelper : OAuthHelperHttpClient, IDisposable
{
    private const string USERINFO_URL = "https://id.jottacloud.com/auth/realms/jottacloud/protocol/openid-connect/userinfo";
    
    public JottacloudAuthHelper(string accessToken)
        : base(accessToken, "jottacloud", HttpClientHelper.CreateClient()) // This will purposefully create a new HttpClient instance per AuthHelper instance
    {
        AutoAuthHeader = true;
        AutoV2 = false; // Jottacloud is not v2 compatible because it generates a new refresh token with every access token refresh and invalidates the old.
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var userinfo = await base.GetJsonDataAsync<UserInfo>(USERINFO_URL, cancellationToken);
            if (userinfo == null || string.IsNullOrEmpty(userinfo.Username))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError, "JottaNoUsername");

            Username = userinfo.Username;
        }
        catch (Exception ex)
        {
            throw new UserInformationException($"Failed to retrieve user information: {ex.Message}", "JottaUserInfoError", ex);
        }
    }

    public string Username { get; private set; }

    private class UserInfo
    {
        [JsonProperty("sub")]
        public string? Subject { get; set; }
        [JsonProperty("email_verified")]
        public bool EmailVerified { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("realm")]
        public string? Realm { get; set; }
        [JsonProperty("preferred_username")]
        public string? PreferredUsername { get; set; } // The numeric internal username, same as Username
        [JsonProperty("given_name")]
        public string? GivenName { get; set; }
        [JsonProperty("family_name")]
        public string? FamilyName { get; set; }
        [JsonProperty("email")]
        public string? Email { get; set; }
        [JsonProperty("username")]
        public string? Username { get; set; } // The numeric internal username, same as PreferredUsername
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}