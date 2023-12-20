//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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

