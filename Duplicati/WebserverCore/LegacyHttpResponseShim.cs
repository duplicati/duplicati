
using HttpServer;
using HttpServer.FormDecoders;
using System.Collections.Specialized;
using System.Net;
using System.Text;

class LegacyHttpResponseShim : HttpServer.IHttpResponse
{
    public Stream Body { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string ProtocolVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool Chunked { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public ConnectionType Connection { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public Encoding Encoding { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int KeepAlive { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public HttpStatusCode Status { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string Reason { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public long ContentLength { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string ContentType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool HeadersSent => throw new NotImplementedException();

    public bool Sent => throw new NotImplementedException();

    public ResponseCookies Cookies => throw new NotImplementedException();

    public void AddHeader(string name, string value)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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