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

using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class OpenStack
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Swift (OpenStack Object Storage). Allowed format is ""openstack://container/folder""."); } }
        public static string DisplayName { get { return LC.L(@"OpenStack Simple Storage"); } }
        public static string MissingOptionError(string optionname) { return LC.L(@"Missing required option: {0}", optionname); }
        public static string PasswordOptionLong(string tenantnameoption) { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD"". If the password is supplied, --{0} must also be set.", tenantnameoption); }
        public static string PasswordOptionShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DomainnameOptionLong { get { return LC.L(@"The domain name of the user used to connect to the server."); } }
        public static string DomainnameOptionShort { get { return LC.L(@"Supply the domain used to connect to the server"); } }
        public static string UsernameOptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string UsernameOptionShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string TenantnameOptionLong { get { return LC.L(@"The Tenant Name is commonly the paying user account name. This option must be supplied when authenticating with a password, but is not required when using an API key."); } }
        public static string TenantnameOptionShort { get { return LC.L(@"Supply the Tenant Name used to connect to the server"); } }
        public static string ApikeyOptionLong { get { return LC.L(@"The API key can be used to connect without supplying a password and tenant ID with some providers."); } }
        public static string ApikeyOptionShort { get { return LC.L(@"Supply the API key used to connect to the server"); } }
        public static string AuthuriOptionLong(string providers) { return LC.L(@"The authentication URL is used to authenticate the user and find the storage service. The URL commonly ends with ""/v2.0"" for v2 and ""/v3"" for v3. Known providers are: {0}{1}", System.Environment.NewLine, providers); }
        public static string AuthuriOptionShort { get { return LC.L(@"Supply the authentication URL"); } }
        public static string VersionOptionLong { get { return LC.L(@"The keystone API version to use. Valid values are 'v2' and 'v3'."); } }
        public static string VersionOptionShort { get { return LC.L(@"The keystone API version to use"); } }
        public static string RegionOptionLong { get { return LC.L(@"By default, the first reported endpoint will be used for file transfers. To select a specific region, provide the region name. If no such region is supported, the default (first reported) endpoint is used."); } }
        public static string RegionOptionShort { get { return LC.L(@"Supply the prefered region for endpoints"); } }

    }
}
