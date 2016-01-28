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
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USAusing Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class OpenStack {        public static string Description { get { return LC.L(@"This backend can read and write data to Swift (OpenStack Object Storage). Supported format is ""openstack://container/folder""."); } }        public static string DisplayName { get { return LC.L(@"OpenStack Simple Storage"); } }        public static string MissingOptionError(string optionname) { return LC.L(@"Missing required option: {0}", optionname); }        public static string PasswordOptionLong(string tenantnameoption) { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD"". If the password is supplied, --{0} must also be set", tenantnameoption); }        public static string PasswordOptionShort { get { return LC.L(@"Supplies the password used to connect to the server"); } }        public static string UsernameOptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }        public static string UsernameOptionShort { get { return LC.L(@"Supplies the username used to connect to the server"); } }        public static string TenantnameOptionLong { get { return LC.L(@"The Tenant Name is commonly the paying user account name. This option must be supplied when authenticating with a password, but is not required when using an API key."); } }        public static string TenantnameOptionShort { get { return LC.L(@"Supplies the Tenant Name used to connect to the server"); } }        public static string ApikeyOptionLong { get { return LC.L(@"The API key can be used to connect without supplying a password and tenant ID with some providers."); } }        public static string ApikeyOptionShort { get { return LC.L(@"Supplies the API key used to connect to the server"); } }        public static string AuthuriOptionLong(string providers) { return LC.L(@"The authentication URL is used to authenticate the user and find the storage service. The URL commonly ends with ""/v2.0"". Known providers are: {0}{1}", System.Environment.NewLine, providers); }        public static string AuthuriOptionShort { get { return LC.L(@"Supplies the authentication URL"); } }        public static string RegionOptionLong { get { return LC.L(@"This option is only used when creating a container, and is used to indicate where the container should be placed. Consult your provider for a list of valid regions, or leave empty for the default region."); } }        public static string RegionOptionShort { get { return LC.L(@"Supplies the region used for creating a container"); } }    }}

