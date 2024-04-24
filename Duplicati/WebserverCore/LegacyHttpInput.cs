using System.Collections;
using HttpServer;
using HttpRequest = Microsoft.AspNetCore.Http.HttpRequest;

namespace Duplicati.WebserverCore;

public class LegacyHttpInput(HttpRequest request) : IHttpInput
{
    public IEnumerator<HttpInputItem> GetEnumerator()
    {
        foreach (var formParam in request.Form)
        {
            yield return new HttpInputItem(formParam.Key, formParam.Value);
        }

        foreach (var formParam in request.Query)
        {
            yield return new HttpInputItem(formParam.Key, formParam.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(string name, string value)
    {
        throw new NotImplementedException();
    }

    public bool Contains(string name)
    {
        foreach (var formParam in request.Form)
        {
            if (formParam.Key == name)
            {
                return true;
            }
        }

        foreach (var formParam in request.Query)
        {
            if (formParam.Key == name)
            {
                return true;
            }
        }

        return false;
    }

    public HttpInputItem this[string name]
    {
        get
        {
            var values = request.HasFormContentType && request.Form.ContainsKey(name) ? request.Form[name] : request.Query[name];
            return new(name, values.First());
        }
    }
}