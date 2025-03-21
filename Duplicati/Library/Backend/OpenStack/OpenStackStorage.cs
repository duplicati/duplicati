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
using Newtonsoft.Json.Converters;
using System.Net;
using System.Text;
using Duplicati.Library.Common.IO;
using System.Runtime.CompilerServices;
using Duplicati.Library.Utility.Options;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.OpenStack
{
    public class OpenStackStorage : IBackend, IStreamingBackend
    {
        private const string DOMAINNAME_OPTION = "openstack-domain-name";
        private const string TENANTNAME_OPTION = "openstack-tenant-name";
        private const string AUTHURI_OPTION = "openstack-authuri";
        private const string VERSION_OPTION = "openstack-version";
        private const string APIKEY_OPTION = "openstack-apikey";
        private const string REGION_OPTION = "openstack-region";

        private const int PAGE_LIMIT = 500;


        private readonly string m_container;
        private readonly string m_prefix;

        private readonly string? m_domainName;
        private readonly string m_username;
        private readonly string? m_password;
        private readonly string m_authUri;
        private readonly string? m_version;
        private readonly string? m_tenantName;
        private readonly string? m_apikey;
        private readonly string? m_region;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        protected string? m_simplestorageendpoint;

        private readonly WebHelper m_helper;
        private OpenStackAuthResponse.TokenClass? m_accessToken;

        public static readonly KeyValuePair<string, string>[] KNOWN_OPENSTACK_PROVIDERS = {
            new KeyValuePair<string, string>("Rackspace US", "https://identity.api.rackspacecloud.com/v2.0"),
            new KeyValuePair<string, string>("Rackspace UK", "https://lon.identity.api.rackspacecloud.com/v2.0"),
            new KeyValuePair<string, string>("OVH Cloud Storage", "https://auth.cloud.ovh.net/v3"),
            new KeyValuePair<string, string>("Selectel Cloud Storage", "https://auth.selcdn.ru"),
            new KeyValuePair<string, string>("Memset Cloud Storage", "https://auth.storage.memset.com"),
            new KeyValuePair<string, string>("Infomaniak Swiss Backup cluster 1", "https://swiss-backup.infomaniak.com/identity/v3"),
            new KeyValuePair<string, string>("Infomaniak Swiss Backup cluster 2", "https://swiss-backup02.infomaniak.com/identity/v3"),
            new KeyValuePair<string, string>("Infomaniak Swiss Backup cluster 3", "https://swiss-backup03.infomaniak.com/identity/v3"),
            new KeyValuePair<string, string>("Infomaniak Swiss Backup cluster 4", "https://swiss-backup04.infomaniak.com/identity/v3"),
            new KeyValuePair<string, string>("Infomaniak Public Cloud 1", "https://api.pub1.infomaniak.cloud/identity/v3"),
            new KeyValuePair<string, string>("Infomaniak Public Cloud 2", "https://api.pub2.infomaniak.cloud/identity/v3"),
            new KeyValuePair<string, string>("Catalyst Cloud - nz-hlz-1 (NZ)", "https://api.nz-hlz-1.catalystcloud.io:5000/v3"),
            new KeyValuePair<string, string>("Catalyst Cloud - nz-por-1 (NZ)", "https://api.nz-por-1.catalystcloud.io:5000/v3"),
        };

        public static readonly KeyValuePair<string, string>[] OPENSTACK_VERSIONS = {
            new KeyValuePair<string, string>("v2.0", "v2"),
            new KeyValuePair<string, string>("v3", "v3"),
        };


        private class Keystone3AuthRequest
        {
            public class AuthContainer
            {
                public Identity? identity { get; set; }
                public Scope? scope { get; set; }
            }

            public class Identity
            {
                [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
                public IdentityMethods[] methods { get; set; }

                [JsonProperty("password", NullValueHandling = NullValueHandling.Ignore)]
                public PasswordBasedRequest? PasswordCredentials { get; set; }

                public Identity()
                {
                    this.methods = new[] { IdentityMethods.password };
                }
            }

            public class Scope
            {
                public Project? project;
            }

            public enum IdentityMethods
            {
                password,
            }

            public class PasswordBasedRequest
            {
                public UserCredentials? user { get; set; }
            }

            public class UserCredentials
            {
                public Domain domain { get; set; }
                public string name { get; set; }
                public string password { get; set; }

                public UserCredentials()
                {
                    domain = null!;
                    name = null!;
                    password = null!;
                }
                public UserCredentials(Domain domain, string name, string password)
                {
                    this.domain = domain;
                    this.name = name;
                    this.password = password;
                }

            }

            public class Domain
            {
                public string? name { get; set; }

                public Domain(string? name)
                {
                    this.name = name;
                }
            }

            public class Project
            {
                public Domain domain { get; set; }
                public string name { get; set; }

                public Project(Domain domain, string name)
                {
                    this.domain = domain;
                    this.name = name;
                }
            }

            public AuthContainer auth { get; set; }

            public Keystone3AuthRequest(string? domain_name, string username, string? password, string? project_name)
            {
                Domain domain = new Domain(domain_name);

                this.auth = new AuthContainer();
                this.auth.identity = new Identity();
                this.auth.identity.PasswordCredentials = new PasswordBasedRequest();
                this.auth.identity.PasswordCredentials.user = new UserCredentials(domain, username, password!);
                this.auth.scope = new Scope();
                this.auth.scope.project = new Project(domain, project_name!);
            }
        }

        private class OpenStackAuthRequest
        {
            public class AuthContainer
            {
                [JsonProperty("RAX-KSKEY:apiKeyCredentials", NullValueHandling = NullValueHandling.Ignore)]
                public ApiKeyBasedRequest? ApiCredentials { get; set; }

                [JsonProperty("passwordCredentials", NullValueHandling = NullValueHandling.Ignore)]
                public PasswordBasedRequest? PasswordCredentials { get; set; }

                [JsonProperty("tenantName", NullValueHandling = NullValueHandling.Ignore)]
                public string? TenantName { get; set; }

                [JsonProperty("token", NullValueHandling = NullValueHandling.Ignore)]
                public TokenBasedRequest? Token { get; set; }

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
                this.auth = new AuthContainer();
                this.auth.TenantName = tenantname;

                if (string.IsNullOrEmpty(apikey))
                {
                    this.auth.PasswordCredentials = new PasswordBasedRequest
                    {
                        username = username,
                        password = password,
                    };
                }
                else
                {
                    this.auth.ApiCredentials = new ApiKeyBasedRequest
                    {
                        username = username,
                        apiKey = apikey
                    };
                }

            }
        }

        private class Keystone3AuthResponse
        {
            public TokenClass? token { get; set; }

            public class EndpointItem
            {
                // 'interface' is a reserved keyword, so we need this decorator to map it
                [JsonProperty(PropertyName = "interface")]
                public string? interface_name { get; set; }
                public string? region { get; set; }
                public string? url { get; set; }
            }

            public class CatalogItem
            {
                public EndpointItem[]? endpoints { get; set; }
                public string? name { get; set; }
                public string? type { get; set; }
            }
            public class TokenClass
            {
                public CatalogItem[]? catalog { get; set; }
                public DateTime? expires_at { get; set; }
            }
        }

        private class OpenStackAuthResponse
        {
            public AccessClass? access { get; set; }

            public class TokenClass
            {
                public string? id { get; set; }
                public DateTime? expires { get; set; }
            }

            public class EndpointItem
            {
                public string? region { get; set; }
                public string? tenantId { get; set; }
                public string? publicURL { get; set; }
                public string? internalURL { get; set; }
            }

            public class ServiceItem
            {
                public string? name { get; set; }
                public string? type { get; set; }
                public EndpointItem[]? endpoints { get; set; }
            }

            public class AccessClass
            {
                public TokenClass? token { get; set; }
                public ServiceItem[]? serviceCatalog { get; set; }
            }

        }

        private class OpenStackStorageItem
        {
            public string? name { get; set; }
            public DateTime? last_modified { get; set; }
            public long? bytes { get; set; }
            public string? content_type { get; set; }
            public string? subdir { get; set; }
        }

        private class WebHelper : JSONWebHelper
        {
            private readonly OpenStackStorage m_parent;

            public WebHelper(OpenStackStorage parent) { m_parent = parent; }

            public override HttpWebRequest CreateRequest(string url, string? method = null)
            {
                var req = base.CreateRequest(url, method);
                req.Headers["X-Auth-Token"] = m_parent.GetAccessToken(CancellationToken.None).Await();
                return req;
            }
        }

        public OpenStackStorage()
        {
            m_container = null!;
            m_prefix = null!;
            m_timeouts = null!;
            m_username = null!;
            m_authUri = null!;
            m_helper = null!;
        }

        public OpenStackStorage(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);

            m_container = uri.Host;
            m_prefix = Util.AppendDirSeparator("/" + uri.Path, "/");
            m_timeouts = TimeoutOptionsHelper.Parse(options);

            // For OpenStack we do not use a leading slash
            if (m_prefix.StartsWith("/", StringComparison.Ordinal))
                m_prefix = m_prefix.Substring(1);

            var auth = AuthOptionsHelper.Parse(options, uri);

            options.TryGetValue(DOMAINNAME_OPTION, out m_domainName);
            options.TryGetValue(TENANTNAME_OPTION, out m_tenantName);
            options.TryGetValue(VERSION_OPTION, out m_version);
            options.TryGetValue(APIKEY_OPTION, out m_apikey);
            options.TryGetValue(REGION_OPTION, out m_region);

            if (!auth.HasUsername)
                throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthUsername), "OpenStackMissingUsername");

            m_username = auth.Username!;
            m_password = auth.Password;
            var authUri = options.GetValueOrDefault(AUTHURI_OPTION);
            if (string.IsNullOrWhiteSpace(authUri))
                throw new UserInformationException(Strings.OpenStack.MissingOptionError(AUTHURI_OPTION), "OpenStackMissingAuthUri");

            m_authUri = authUri;
            if (string.IsNullOrWhiteSpace(m_version) && m_authUri.EndsWith("/v3", StringComparison.OrdinalIgnoreCase))
                m_version = "v3";

            switch (m_version)
            {
                case "v3":
                    if (string.IsNullOrWhiteSpace(m_password))
                        throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthPassword), "OpenStackMissingPassword");
                    if (string.IsNullOrWhiteSpace(m_domainName))
                        throw new UserInformationException(Strings.OpenStack.MissingOptionError(DOMAINNAME_OPTION), "OpenStackMissingDomainName");
                    if (string.IsNullOrWhiteSpace(m_tenantName))
                        throw new UserInformationException(Strings.OpenStack.MissingOptionError(TENANTNAME_OPTION), "OpenStackMissingTenantName");
                    break;
                case "v2":
                default:
                    if (string.IsNullOrWhiteSpace(m_apikey))
                    {
                        if (string.IsNullOrWhiteSpace(m_password))
                            throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthPassword), "OpenStackMissingPassword");
                        if (string.IsNullOrWhiteSpace(m_tenantName))
                            throw new UserInformationException(Strings.OpenStack.MissingOptionError(TENANTNAME_OPTION), "OpenStackMissingTenantName");
                    }
                    break;
            }

            m_helper = new WebHelper(this);
        }

        protected async Task<string> GetAccessToken(CancellationToken cancelToken)
        {
            if (m_accessToken == null || (m_accessToken.expires.HasValue && (m_accessToken.expires.Value - DateTime.UtcNow).TotalSeconds < 30))
                await GetAuthResponse(cancelToken).ConfigureAwait(false);

            return m_accessToken!.id!;
        }

        private static string JoinUrls(string uri, string fragment)
        {
            fragment = fragment ?? "";
            return uri + (uri.EndsWith("/", StringComparison.Ordinal) ? "" : "/") + (fragment.StartsWith("/", StringComparison.Ordinal) ? fragment.Substring(1) : fragment);
        }
        private static string JoinUrls(string uri, string fragment1, string fragment2)
        {
            return JoinUrls(JoinUrls(uri, fragment1), fragment2);
        }

        private Task GetAuthResponse(CancellationToken cancellationToken)
        {
            switch (this.m_version)
            {
                case "v3":
                    return GetKeystone3AuthResponse(cancellationToken);
                case "v2":
                default:
                    return GetOpenstackAuthResponse(cancellationToken);
            }
        }

        private async Task<Keystone3AuthResponse> GetKeystone3AuthResponse(CancellationToken cancellationToken)
        {
            var helper = new JSONWebHelper();

            var req = helper.CreateRequest(JoinUrls(m_authUri, "auth/tokens"));
            req.Accept = "application/json";
            req.Method = "POST";

            var data = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(
                    new Keystone3AuthRequest(m_domainName, m_username, m_password, m_tenantName)
                ));

            req.ContentLength = data.Length;
            req.ContentType = "application/json; charset=UTF-8";

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, async ct =>
            {
                using (var rs = req.GetRequestStream())
                    await rs.WriteAsync(data, 0, data.Length, ct);
            }).ConfigureAwait(false);


            var http_response = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, ct => req.GetResponse()).ConfigureAwait(false);

            var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, ct =>
            {
                using (var reader = new StreamReader(http_response.GetResponseStream()))
                    return JsonConvert.DeserializeObject<Keystone3AuthResponse>(reader.ReadToEnd())
                        ?? throw new Exception("Failed to parse response");
            }).ConfigureAwait(false);

            if (resp.token == null)
                throw new Exception("No token received");

            var token = http_response.Headers["X-Subject-Token"];
            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("No token received");

            this.m_accessToken = new OpenStackAuthResponse.TokenClass()
            {
                id = token,
                expires = resp.token.expires_at
            };

            // Grab the endpoint now that we have received it anyway
            var fileservice = (resp.token.catalog ?? []).FirstOrDefault(x => string.Equals(x.type, "object-store", StringComparison.OrdinalIgnoreCase));
            if (fileservice == null)
                throw new Exception("No object-store service found, is this service supported by the provider?");

            if (fileservice.endpoints == null || fileservice.endpoints.Length == 0)
                throw new Exception("No endpoints found for object-store service");

            var endpoint = fileservice.endpoints.FirstOrDefault(x => (string.Equals(m_region, x.region) && string.Equals(x.interface_name, "public", StringComparison.OrdinalIgnoreCase))) ?? fileservice.endpoints.First();
            m_simplestorageendpoint = endpoint.url;

            return resp;
        }

        private async Task<OpenStackAuthResponse> GetOpenstackAuthResponse(CancellationToken cancellationToken)
        {
            var helper = new JSONWebHelper();

            var req = helper.CreateRequest(JoinUrls(m_authUri, "tokens"));
            req.Accept = "application/json";
            req.Method = "POST";

            var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, ct => helper.ReadJSONResponse<OpenStackAuthResponse>(
                req,
                new OpenStackAuthRequest(m_tenantName, m_username, m_password, m_apikey)
            )).ConfigureAwait(false);

            if (resp.access?.token == null)
                throw new Exception("No token received");

            m_accessToken = resp.access.token;

            // Grab the endpoint now that we have received it anyway
            var fileservice = (resp.access.serviceCatalog ?? []).FirstOrDefault(x => string.Equals(x.type, "object-store", StringComparison.OrdinalIgnoreCase));
            if (fileservice == null)
                throw new Exception("No object-store service found, is this service supported by the provider?");

            var fileserviceendpoints = fileservice.endpoints ?? [];
            var endpoint = fileserviceendpoints.FirstOrDefault(x => string.Equals(m_region, x.region))
                ?? fileserviceendpoints.FirstOrDefault();

            if (endpoint == null)
                throw new Exception("No endpoint found for object-store service");

            m_simplestorageendpoint = endpoint.publicURL;

            return resp;
        }

        protected async Task<string> GetSimpleStorageEndPoint(CancellationToken cancelToken)
        {
            if (m_simplestorageendpoint == null)
                await GetAuthResponse(cancelToken);

            return m_simplestorageendpoint!;
        }

        #region IStreamingBackend implementation
        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Utility.Uri.UrlPathEncode(m_prefix + remotename));
            using (var ts = stream.ObserveReadTimeout(m_timeouts.ShortTimeout, false))
            using (await m_helper.GetResponseAsync(url, cancelToken, ts, "PUT").ConfigureAwait(false))
            { }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Utility.Uri.UrlPathEncode(m_prefix + remotename));

            try
            {
                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_helper.GetResponse(url)).ConfigureAwait(false))
                using (var rs = AsyncHttpRequest.TrySetTimeout(await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => resp.GetResponseStream()).ConfigureAwait(false)))
                using (var ts = rs.ObserveReadTimeout(m_timeouts.ShortTimeout))
                    await Utility.Utility.CopyStreamAsync(ts, stream, cancelToken).ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException();
                else
                    throw;
            }

        }
        #endregion
        #region IBackend implementation

        private async Task<T> HandleListExceptions<T>(Func<Task<T>> func)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && (response.StatusCode == HttpStatusCode.NotFound))
                    throw new FolderMissingException();
                else
                    throw;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var plainurl = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container) + string.Format("?format=json&delimiter=/&limit={0}", PAGE_LIMIT);
            if (!string.IsNullOrEmpty(m_prefix))
                plainurl += "&prefix=" + Utility.Uri.UrlEncode(m_prefix);

            var url = plainurl;

            while (true)
            {
                var req = m_helper.CreateRequest(url);
                req.Accept = "application/json";

                var items = await HandleListExceptions(async () => await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct => m_helper.ReadJSONResponseAsync<OpenStackStorageItem[]>(req, ct))).ConfigureAwait(false);
                foreach (var n in items)
                {
                    var name = n.name;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (name.StartsWith(m_prefix, StringComparison.Ordinal))
                        name = name.Substring(m_prefix.Length);

                    if (n.bytes == null)
                        yield return new FileEntry(name);
                    else if (n.last_modified == null)
                        yield return new FileEntry(name, n.bytes.Value);
                    else
                        yield return new FileEntry(name, n.bytes.Value, n.last_modified.Value, n.last_modified.Value);
                }

                if (items.Length != PAGE_LIMIT)
                    yield break;

                // Prepare next listing entry
                url = plainurl + string.Format("&marker={0}", Library.Utility.Uri.UrlEncode(items.Last().name));
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename));
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_helper.ReadJSONResponseAsync<object>(url, ct, null, "DELETE")).ConfigureAwait(false);
        }
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container);
            using (await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_helper.GetResponseAsync(url, cancelToken, null, "PUT")).ConfigureAwait(false))
            { }
        }
        public string DisplayName => Strings.OpenStack.DisplayName;
        public string ProtocolKey => "openstack";
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                var authuris = new StringBuilder();
                foreach (var s in KNOWN_OPENSTACK_PROVIDERS)
                    authuris.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                return [
                    new CommandLineArgument(DOMAINNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.DomainnameOptionShort, Strings.OpenStack.DomainnameOptionLong),
                    new CommandLineArgument(AuthOptionsHelper.AuthUsername, CommandLineArgument.ArgumentType.String, Strings.OpenStack.UsernameOptionShort, Strings.OpenStack.UsernameOptionLong),
                    new CommandLineArgument(AuthOptionsHelper.AuthPassword, CommandLineArgument.ArgumentType.Password, Strings.OpenStack.PasswordOptionShort, Strings.OpenStack.PasswordOptionLong(TENANTNAME_OPTION)),
                    new CommandLineArgument(TENANTNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.TenantnameOptionShort, Strings.OpenStack.TenantnameOptionLong),
                    new CommandLineArgument(APIKEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.OpenStack.ApikeyOptionShort, Strings.OpenStack.ApikeyOptionLong),
                    new CommandLineArgument(AUTHURI_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.AuthuriOptionShort, Strings.OpenStack.AuthuriOptionLong(authuris.ToString())),
                    new CommandLineArgument(VERSION_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.VersionOptionShort, Strings.OpenStack.VersionOptionLong),
                    new CommandLineArgument(REGION_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.RegionOptionShort, Strings.OpenStack.RegionOptionLong),
                    .. TimeoutOptionsHelper.GetOptions(),
                ];
            }
        }
        public string Description => Strings.OpenStack.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] {
            new System.Uri(m_authUri).Host,
            string.IsNullOrWhiteSpace(m_simplestorageendpoint) ? null : new System.Uri(m_simplestorageendpoint).Host
        }
        .WhereNotNullOrWhiteSpace()
        .ToArray());

        #endregion
        #region IDisposable implementation
        public void Dispose()
        {
        }
        #endregion
    }
}

