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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Exception = System.Exception;
using Uri = Duplicati.Library.Utility.Uri;

namespace Duplicati.Library.Backend.OpenStack;

public class OpenStackStorage : IStreamingBackend
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
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Lazy cached HttpClient
    /// </summary>
    private readonly HttpClient m_httpClient;

    /// <summary>
    /// The simplestorage endpoint URL, set after authentication.
    /// </summary>
    private string? m_simplestorageendpoint;

    private readonly OpenStackWebHelper m_helper;
    private OpenStackAuthResponse.TokenClass? m_accessToken;

    public static readonly KeyValuePair<string, string?>[] KnownOpenstackProviders =
    [
        new("Rackspace US", "https://identity.api.rackspacecloud.com/v2.0"),
        new("Rackspace UK", "https://lon.identity.api.rackspacecloud.com/v2.0"),
        new("OVH Cloud Storage", "https://auth.cloud.ovh.net/v3"),
        new("Selectel Cloud Storage", "https://auth.selcdn.ru"),
        new("Memset Cloud Storage", "https://auth.storage.memset.com"),
        new("Infomaniak Swiss Backup cluster 1", "https://swiss-backup.infomaniak.com/identity/v3"),
        new("Infomaniak Swiss Backup cluster 2", "https://swiss-backup02.infomaniak.com/identity/v3"),
        new("Infomaniak Swiss Backup cluster 3", "https://swiss-backup03.infomaniak.com/identity/v3"),
        new("Infomaniak Swiss Backup cluster 4", "https://swiss-backup04.infomaniak.com/identity/v3"),
        new("Infomaniak Public Cloud 1", "https://api.pub1.infomaniak.cloud/identity/v3"),
        new("Infomaniak Public Cloud 2", "https://api.pub2.infomaniak.cloud/identity/v3"),
        new("Catalyst Cloud - nz-hlz-1 (NZ)", "https://api.nz-hlz-1.catalystcloud.io:5000/v3"),
        new("Catalyst Cloud - nz-por-1 (NZ)", "https://api.nz-por-1.catalystcloud.io:5000/v3")
    ];

    public static readonly KeyValuePair<string, string?>[] OpenstackVersions =
    [
        new("v2.0", "v2"),
        new("v3", "v3")
    ];

    public OpenStackStorage()
    {
        m_container = null!;
        m_prefix = null!;
        _timeouts = null!;
        m_username = null!;
        m_authUri = null!;
        m_helper = null!;
        m_simplestorageendpoint = null!;
        m_httpClient = null!;
    }

    public OpenStackStorage(string url, Dictionary<string, string?> options)
    {
        var uri = new Uri(url);

        m_container = uri.Host;
        m_prefix = Util.AppendDirSeparator("/" + uri.Path, "/");
        _timeouts = TimeoutOptionsHelper.Parse(options);

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
            throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthUsernameOption), "OpenStackMissingUsername");

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
                    throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthPasswordOption), "OpenStackMissingPassword");
                if (string.IsNullOrWhiteSpace(m_domainName))
                    throw new UserInformationException(Strings.OpenStack.MissingOptionError(DOMAINNAME_OPTION), "OpenStackMissingDomainName");
                if (string.IsNullOrWhiteSpace(m_tenantName))
                    throw new UserInformationException(Strings.OpenStack.MissingOptionError(TENANTNAME_OPTION), "OpenStackMissingTenantName");
                break;
            default:
                if (string.IsNullOrWhiteSpace(m_apikey))
                {
                    if (string.IsNullOrWhiteSpace(m_password))
                        throw new UserInformationException(Strings.OpenStack.MissingOptionError(AuthOptionsHelper.AuthPasswordOption), "OpenStackMissingPassword");
                    if (string.IsNullOrWhiteSpace(m_tenantName))
                        throw new UserInformationException(Strings.OpenStack.MissingOptionError(TENANTNAME_OPTION), "OpenStackMissingTenantName");
                }
                break;
        }

        m_httpClient = HttpClientHelper.CreateClient();
        m_httpClient.Timeout = Timeout.InfiniteTimeSpan;
        m_helper = new OpenStackWebHelper(this, m_httpClient);
    }

    internal async Task<string> GetAccessToken(CancellationToken cancelToken)
    {
        if (m_accessToken == null || (m_accessToken.expires.HasValue && (m_accessToken.expires.Value - DateTime.UtcNow).TotalSeconds < 30))
            await GetAuthResponse(cancelToken).ConfigureAwait(false);

        return m_accessToken?.id!;
    }

    private static string JoinUrls(string uri, string fragment)
    {
        fragment = fragment ?? "";
        return uri + (uri.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + (fragment.StartsWith("/", StringComparison.Ordinal) ? fragment.Substring(1) : fragment);
    }
    private static string JoinUrls(string uri, string fragment1, string fragment2)
    {
        return JoinUrls(JoinUrls(uri, fragment1), fragment2);
    }

    /// <summary>
    /// Gets the authentication response from OpenStack.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use for the request.</param>
    /// <returns>The simplestorage endpoint URL.</returns>
    private async Task<string> GetAuthResponse(CancellationToken cancellationToken)
    {
        if (m_version == "v3")
            (m_accessToken, m_simplestorageendpoint) = await GetKeystone3AuthResponse(cancellationToken).ConfigureAwait(false);
        else
            (m_accessToken, m_simplestorageendpoint) = await GetOpenstackAuthResponse(cancellationToken).ConfigureAwait(false);

        return m_simplestorageendpoint ?? throw new Exception("No endpoint received from OpenStack authentication");
    }

    /// <summary>
    /// Gets the Keystone v3 authentication response from OpenStack.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use for the request.</param>
    /// <returns>The authentication token and the simplestorage endpoint URL.</returns>
    private async Task<(OpenStackAuthResponse.TokenClass, string? endpoint)> GetKeystone3AuthResponse(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, JoinUrls(m_authUri, "auth/tokens"));
        request.Headers.UserAgent.ParseAdd(
            $"Duplicati CloudStack Client {Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");

        var json = System.Text.Json.JsonSerializer.Serialize(
            new Keystone3AuthRequest(m_domainName, m_username, m_password, m_tenantName),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

        // NOTE: We need to control the serialization to ensure the correct format
        // don't use JsonContent.Create() as it will not serialize the request correctly
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        (var parsedResult, var token) = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken,
                async ct =>
                {
                    using var resp = await m_httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"Failed to fetch region endpoint: {await resp.Content.ReadAsStringAsync()}");

                    var parsedResult = (await resp.Content.ReadFromJsonAsync<Keystone3AuthResponse>(ct).ConfigureAwait(false))
                        ?? throw new Exception("Failed to parse response"); ;

                    var token = resp.Headers.GetValues("X-Subject-Token").FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(token))
                        throw new Exception("No token received");

                    return (parsedResult, token);
                })
            .ConfigureAwait(false);

        if (parsedResult.token == null)
            throw new Exception("No token received");

        // Grab the endpoint now that we have received it anyway
        var fileService = (parsedResult.token.catalog ?? []).FirstOrDefault(x => string.Equals(x.type, "object-store", StringComparison.OrdinalIgnoreCase));
        if (fileService == null)
            throw new Exception("No object-store service found, is this service supported by the provider?");

        if (fileService.endpoints == null || fileService.endpoints.Length == 0)
            throw new Exception("No endpoints found for object-store service");

        var endpoint = fileService.endpoints.FirstOrDefault(x => string.Equals(m_region, x.region) && string.Equals(x.@interface, "public", StringComparison.OrdinalIgnoreCase)) ?? fileService.endpoints.First();
        m_simplestorageendpoint = endpoint.url;

        var result = new OpenStackAuthResponse.TokenClass
        {
            id = token,
            expires = parsedResult.token.expires_at
        };

        return (result, m_simplestorageendpoint);
    }

    /// <summary>
    /// Gets the OpenStack authentication response.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use for the request.</param>
    /// <returns>A tuple containing the authentication token and the simplestorage endpoint URL.</returns>
    private async Task<(OpenStackAuthResponse.TokenClass, string? endpoint)> GetOpenstackAuthResponse(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, JoinUrls(m_authUri, "tokens"));
        request.Headers.UserAgent.ParseAdd(
            $"Duplicati CloudStack Client {Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");

        var json = System.Text.Json.JsonSerializer.Serialize(
            new OpenStackAuthRequest(m_tenantName, m_username, m_password, m_apikey),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

        // NOTE: We need to control the serialization to ensure the correct format
        // don't use JsonContent.Create() as it will not serialize the request correctly
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var parsedResult = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken,
                async ct =>
                {
                    using var resp = await m_httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    if (resp.StatusCode != HttpStatusCode.OK)
                        throw new Exception("Failed to fetch region endpoint");

                    return (await resp.Content.ReadFromJsonAsync<OpenStackAuthResponse>(ct).ConfigureAwait(false))
                        ?? throw new Exception("Failed to parse response");
                })
            .ConfigureAwait(false);

        // Grab the endpoint now that we have received it anyway
        var fileservice = (parsedResult.access?.serviceCatalog ?? []).FirstOrDefault(x => string.Equals(x.type, "object-store", StringComparison.OrdinalIgnoreCase));
        if (fileservice == null)
            throw new Exception("No object-store service found, is this service supported by the provider?");

        var fileserviceendpoints = fileservice.endpoints ?? [];
        var endpoint = fileserviceendpoints.FirstOrDefault(x => string.Equals(m_region, x.region))
                       ?? fileserviceendpoints.FirstOrDefault();

        if (endpoint == null)
            throw new Exception("No endpoint found for object-store service");

        return (parsedResult.access?.token ?? throw new Exception("No token received"), endpoint.publicURL);
    }

    /// <summary>
    /// Gets the simplestorage endpoint URL.
    /// </summary>
    /// <param name="cancelToken">The cancellation token to use for the request.</param>
    /// <returns>The simplestorage endpoint URL.</returns>
    private async Task<string> GetSimpleStorageEndPoint(CancellationToken cancelToken)
    {
        if (m_simplestorageendpoint == null)
            return await GetAuthResponse(cancelToken);

        return m_simplestorageendpoint;
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Uri.UrlPathEncode(m_prefix + remotename));
        using var req = m_helper.CreateRequest(url, "PUT");
        await using var ts = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);

        req.Content = new StreamContent(ts);
        req.Content.Headers.Add("Content-Type", "application/octet-stream");
        req.Content.Headers.Add("Content-Length", stream.Length.ToString());
        using var response = await m_httpClient.UploadStream(req, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Uri.UrlPathEncode(m_prefix + remotename));

        try
        {
            using var req = m_helper.CreateRequest(url);
            using var resp = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => m_helper.GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
            await using var rs = await resp.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
            await using var ts = rs.ObserveReadTimeout(_timeouts.ReadWriteTimeout);
            await Utility.Utility.CopyStreamAsync(ts, stream, cancelToken).ConfigureAwait(false);
        }
        catch (HttpRequestException wex)
        {
            if (wex.StatusCode == HttpStatusCode.NotFound)
                throw new FileMissingException();
            throw;
        }
    }

    /// <summary>
    /// Handles exceptions that may occur during listing operations.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the function.</typeparam>
    /// <param name="func">The function to execute that may throw exceptions.</param>
    /// <returns>The result of the function if successful.</returns>
    private async Task<T> HandleListExceptions<T>(Func<Task<T>> func)
    {
        try
        {
            return await func().ConfigureAwait(false);
        }
        catch (HttpRequestException wex)
        {
            if (wex.StatusCode == HttpStatusCode.NotFound)
                throw new FolderMissingException();
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {
        var plainurl = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container) + string.Format("?format=json&delimiter=/&limit={0}", PAGE_LIMIT);
        if (!string.IsNullOrEmpty(m_prefix))
            plainurl += "&prefix=" + Uri.UrlEncode(m_prefix);

        var url = plainurl;

        while (true)
        {
            using var req = m_helper.CreateRequest(url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var items = await HandleListExceptions(async () => await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken, ct => m_helper.ReadJsonResponseAsync<OpenStackStorageItem[]>(req, ct))).ConfigureAwait(false);
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
            url = plainurl + $"&marker={Uri.UrlEncode(items.Last().name)}";
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using FileStream fs = File.OpenRead(filename);
        await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using FileStream fs = File.Create(filename);
        await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container, Uri.UrlPathEncode(m_prefix + remotename));
        using var req = m_helper.CreateRequest(url, "DELETE");
        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => m_helper.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestReadWritePermissionsAsync(cancelToken);

    /// <inheritdoc />
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        var url = JoinUrls(await GetSimpleStorageEndPoint(cancelToken).ConfigureAwait(false), m_container);
        using var req = m_helper.CreateRequest(url, "PUT");
        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => m_helper.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public string DisplayName => Strings.OpenStack.DisplayName;

    /// <inheritdoc />
    public string ProtocolKey => "openstack";

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
    {
        get
        {
            var authuris = new StringBuilder();
            foreach (var s in KnownOpenstackProviders)
                authuris.AppendLine($"{s.Key}: {s.Value}");

            return [
                new CommandLineArgument(DOMAINNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.DomainnameOptionShort, Strings.OpenStack.DomainnameOptionLong),
                new CommandLineArgument(AuthOptionsHelper.AuthUsernameOption, CommandLineArgument.ArgumentType.String, Strings.OpenStack.UsernameOptionShort, Strings.OpenStack.UsernameOptionLong),
                new CommandLineArgument(AuthOptionsHelper.AuthPasswordOption, CommandLineArgument.ArgumentType.Password, Strings.OpenStack.PasswordOptionShort, Strings.OpenStack.PasswordOptionLong(TENANTNAME_OPTION)),
                new CommandLineArgument(TENANTNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.TenantnameOptionShort, Strings.OpenStack.TenantnameOptionLong),
                new CommandLineArgument(APIKEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.OpenStack.ApikeyOptionShort, Strings.OpenStack.ApikeyOptionLong),
                new CommandLineArgument(AUTHURI_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.AuthuriOptionShort, Strings.OpenStack.AuthuriOptionLong(authuris.ToString())),
                new CommandLineArgument(VERSION_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.VersionOptionShort, Strings.OpenStack.VersionOptionLong),
                new CommandLineArgument(REGION_OPTION, CommandLineArgument.ArgumentType.String, Strings.OpenStack.RegionOptionShort, Strings.OpenStack.RegionOptionLong),
                .. TimeoutOptionsHelper.GetOptions(),
            ];
        }
    }

    /// <inheritdoc />
    public string Description => Strings.OpenStack.Description;

    /// <inheritdoc />
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] {
            new System.Uri(m_authUri).Host,
            string.IsNullOrWhiteSpace(m_simplestorageendpoint) ? null : new System.Uri(m_simplestorageendpoint).Host
        }
        .WhereNotNullOrWhiteSpace()
        .ToArray());

    public void Dispose()
    {
    }
}