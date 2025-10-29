using Duplicati.Library.Backend.Rapidgator;
using System;
using System.Collections.Specialized;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Duplicati.Library.Backend.OpenStack;

internal class RapidgatorHttpClientHelper(RapidgatorBackend parent, HttpClient httpClient) : 
  JsonWebHelperHttpClient(httpClient)
{
  public override async Task<HttpRequestMessage> CreateRequestAsync(
    string url,
    HttpMethod method,
    CancellationToken cancelToken)
  {
    HttpRequestMessage request = await base.CreateRequestAsync(url, method, cancelToken);
    UriBuilder uriBuilder = new UriBuilder(request.RequestUri);
    NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
    NameValueCollection nameValueCollection = query;
    string str = await parent.GetAccessTokenAsync(CancellationToken.None);
    nameValueCollection["token"] = str;
    nameValueCollection = (NameValueCollection) null;
    str = (string) null;
    uriBuilder.Query = query.ToString();
    request.RequestUri = uriBuilder.Uri;
    HttpRequestMessage requestAsync = request;
    request = (HttpRequestMessage) null;
    uriBuilder = (UriBuilder) null;
    query = (NameValueCollection) null;
    return requestAsync;
  }
}
