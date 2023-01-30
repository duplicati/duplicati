
using HttpServer;
using HttpServer.FormDecoders;
using HttpServer.Sessions;
using System.Collections.Specialized;
using System.Net;
using System.Text;

class LegacyHttpSessionShim : HttpServer.Sessions.IHttpSession
{
    public object this[string name] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string Id => throw new NotImplementedException();

    public DateTime Accessed { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public int Count => throw new NotImplementedException();

    public IEnumerable<string> Keys => throw new NotImplementedException();

    public event HttpSessionClearedHandler BeforeClear;

    public void Clear()
    {
        throw new NotImplementedException();
    }

    public void Clear(bool expires)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}