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
using Newtonsoft.Json;

namespace Duplicati.Library.Backend
{
    public class JottacloudAuthHelper : OAuthHelper
    {
        private const string USERINFO_URL = "https://id.jottacloud.com/auth/realms/jottacloud/protocol/openid-connect/userinfo";
        private string m_username;

        public JottacloudAuthHelper(string accessToken)
            : base(accessToken, "jottacloud")
        {
            base.AutoAuthHeader = true;
            base.AutoV2 = false; // Jottacloud is not v2 compatible because it generates a new refresh token with every access token refresh and invalidates the old.

            var userinfo = GetJSONData<UserInfo>(USERINFO_URL);
            if (userinfo == null || string.IsNullOrEmpty(userinfo.Username))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError, "JottaNoUsername");
            m_username = userinfo.Username;
        }

        public string Username
        {
            get
            {
                return m_username;
            }
        }

        private class UserInfo
        {
            [JsonProperty("sub")]
            public string Subject { get; set; }
            [JsonProperty("email_verified")]
            public bool EmailVerified { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("realm")]
            public string Realm { get; set; }
            [JsonProperty("preferred_username")]
            public string PreferredUsername { get; set; } // The numeric internal username, same as Username
            [JsonProperty("given_name")]
            public string GivenName { get; set; }
            [JsonProperty("family_name")]
            public string FamilyName { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
            [JsonProperty("username")]
            public string Username { get; set; } // The numeric internal username, same as PreferredUsername
        }
    }

}

