
using Duplicati.Library.Utility;
using HttpServer;
using HttpServer.FormDecoders;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
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

    public RequestCookies Cookies => new RequestCookies(request.Headers.Cookie);

    public HttpForm Form
    {
        get
        {
            var form = new HttpForm();

            if (request.ContentType != null && (request.ContentType.StartsWith("multipart/form-data", true, CultureInfo.InvariantCulture) || request.ContentType.StartsWith("application/x-www-form-urlencoded", true, CultureInfo.InvariantCulture) ))
            {

                foreach (var kvp in request.Form)
                {
                    form.Add(kvp.Key, kvp.Value);
                }

                //Files
                foreach (var file in request.Form.Files)
                {
                    // Generate a temp file *CRY* I can't believe duplicati did this
                    string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HttpServer");
                    string tempFile = Path.Combine(path, Math.Abs(file.FileName.GetHashCode()) + ".tmp");

                    // If the file exists generate a new filename
                    while (File.Exists(tempFile))
                        tempFile = Path.Combine(path, Math.Abs(file.FileName.GetHashCode() + 1) + ".tmp");

                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                    using (var memoryStream = new MemoryStream())
                    {
                        file.CopyTo(memoryStream);
                        File.WriteAllBytes(tempFile, memoryStream.ToArray());
                    }

                    form.AddFile(new HttpFile(file.Name, tempFile, file.ContentType, file.FileName));
                }
            }
            return form;
        }
    }

    public NameValueCollection Headers
    {
        get
        {
            var headers = new NameValueCollection();
            foreach (var pair in request.Headers)
            {
                foreach (var value in pair.Value)
                {
                    headers.Add(pair.Key, value);
                }
            }
            return headers;
        }
    }

    public string HttpVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public bool IsAjax => throw new NotImplementedException();

    public string Method { get => request.Method; set => throw new NotImplementedException(); }

    public HttpParam Param => throw new NotImplementedException();

    public HttpInput QueryString
    {
        get
        {
            var input = new HttpInput("QueryString");
            foreach (var kvp in request.Query) { input.Add(kvp.Key, kvp.Value); }
            return input;
        }
    }

    public bool Secure => throw new NotImplementedException();

    public IPEndPoint RemoteEndPoint => throw new NotImplementedException();

    public System.Uri Uri { get => new System.Uri(request.GetEncodedUrl()); set => throw new NotImplementedException(); }

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