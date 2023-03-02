
using HttpServer;
using HttpServer.FormDecoders;
using System.Collections.Specialized;
using System.Net;
using HttpRequest = Microsoft.AspNetCore.Http.HttpRequest;

class LegacyHttpRequestShim : HttpServer.IHttpRequest
{
    HttpRequest request;
    public LegacyHttpRequestShim(HttpRequest request) { this.request = request; }

    public string[] AcceptTypes => throw new NotImplementedException();

    public Stream Body { get => request.Body; set => throw new NotImplementedException(); }

    public bool BodyIsComplete => throw new NotImplementedException();

    public ConnectionType Connection { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int ContentLength { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public RequestCookies Cookies => throw new NotImplementedException();

    public HttpForm Form => throw new NotImplementedException();

    public NameValueCollection Headers => throw new NotImplementedException();

    public string HttpVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool IsAjax => throw new NotImplementedException();

    public string Method { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public HttpParam Param => throw new NotImplementedException();

    public HttpInput QueryString { get {
            var input = new HttpInput("QueryString");
            foreach (var kvp in request.Query ) { input.Add(kvp.Key, kvp.Value); }
            return input;
        } 
    }

    public bool Secure => throw new NotImplementedException();

    public IPEndPoint RemoteEndPoint => throw new NotImplementedException();

    public Uri Uri { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string[] UriParts => throw new NotImplementedException();

    public string UriPath { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void AddHeader(string name, string value)
    {
        throw new NotImplementedException();
    }

    public int AddToBody(byte[] bytes, int offset, int length)
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        throw new NotImplementedException();
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public IHttpResponse CreateResponse(IHttpClientContext context)
    {
        throw new NotImplementedException();
    }

    public void DecodeBody(FormDecoderProvider providers)
    {
        throw new NotImplementedException();
    }

    public byte[] GetBody()
    {
        throw new NotImplementedException();
    }

    public void SetCookies(RequestCookies cookies)
    {
        throw new NotImplementedException();
    }
}