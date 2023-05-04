
using HttpServer;
using HttpServer.FormDecoders;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using HttpResponse = Microsoft.AspNetCore.Http.HttpResponse;

class LegacyHttpResponseShim : HttpServer.IHttpResponse
{
    HttpResponse response;
    public LegacyHttpResponseShim(HttpResponse response) { this.response = response; }
    public Stream Body { get => response.Body; set => throw new NotImplementedException(); }
    public string ProtocolVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool Chunked { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public ConnectionType Connection { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public Encoding Encoding { get => Encoding.UTF8; set => throw new NotImplementedException(); }
    public int KeepAlive { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public HttpStatusCode Status { get => throw new NotImplementedException(); set => response.StatusCode = (int)value; }
    public string Reason { get => throw new NotImplementedException(); set { /*NOP*/ } }
    public long ContentLength { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string ContentType { get => throw new NotImplementedException(); set => response.ContentType = value; }

    public bool HeadersSent => response.HasStarted;

    public bool Sent => throw new NotImplementedException();

    public ResponseCookies Cookies => throw new NotImplementedException();

    public void AddHeader(string name, string value)
    {
        if (response.Headers.ContainsKey(name))
        {
            response.Headers.Remove(name);
        }
        response.Headers.Add(name, value);
    }

    public void Redirect(Uri uri)
    {
        throw new NotImplementedException();
    }

    public void Redirect(string url)
    {
        throw new NotImplementedException();
    }

    public void Send()
    {
        response.StartAsync();
    }

    public void SendBody(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public void SendBody(byte[] buffer)
    {
        throw new NotImplementedException();
    }

    public void SendHeaders()
    {
        throw new NotImplementedException();
    }
}