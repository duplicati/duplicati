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

namespace Duplicati.Library.Backend.OpenStack;

internal class OpenStackAuthRequest
{
    public class AuthContainer
    {
        [System.Text.Json.Serialization.JsonPropertyName("RAX-KSKEY:apiKeyCredentials")]
        public ApiKeyBasedRequest? ApiCredentials { get; set; }

        public PasswordBasedRequest? passwordCredentials { get; set; }

        public string? tenantName { get; set; }

        public TokenBasedRequest? token { get; set; }

    }

    public class ApiKeyBasedRequest
    {
        public string? username { get; set; }
        public string? apiKey { get; set; }
    }

    public class PasswordBasedRequest
    {
        public string? username { get; set; }
        public string? password { get; set; }
        public string? tenantName { get; set; }
    }

    public class TokenBasedRequest
    {
        public string? id { get; set; }
    }

    public AuthContainer auth { get; set; }

    public OpenStackAuthRequest(string? tenantname, string username, string? password, string? apikey)
    {
        auth = new AuthContainer
        {
            tenantName = tenantname
        };

        if (string.IsNullOrEmpty(apikey))
        {
            auth.passwordCredentials = new PasswordBasedRequest
            {
                username = username,
                password = password,
            };
        }
        else
        {
            auth.ApiCredentials = new ApiKeyBasedRequest
            {
                username = username,
                apiKey = apikey
            };
        }

    }
}