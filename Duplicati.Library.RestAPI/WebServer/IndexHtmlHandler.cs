// Copyright (C) 2024, The Duplicati Team
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
using System;
using System.Linq;
using HttpServer;
using HttpServer.HttpModules;
using HttpServer.Exceptions;
using Duplicati.Library.Common.IO;

namespace Duplicati.Server.WebServer
{
    internal class IndexHtmlHandler : HttpModule
    {
        private readonly string m_webroot;

        private static readonly string[] ForbiddenChars = new string[] {"\\", "..", ":"}.Union(from n in System.IO.Path.GetInvalidPathChars() select n.ToString()).Distinct().ToArray();
        private static readonly string DirSep = Util.DirectorySeparatorString;

        public IndexHtmlHandler(string webroot) { m_webroot = webroot; }

        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            var path = this.GetPath(request.Uri);
            var html = System.IO.Path.Combine(path, "index.html");
            var htm = System.IO.Path.Combine(path, "index.htm");

            if (System.IO.Directory.Exists(path) && (System.IO.File.Exists(html) || System.IO.File.Exists(htm)))
            {
                if (!request.Uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                {
                    response.Redirect(request.Uri.AbsolutePath + "/");
                    return true;
                }

                response.Status = System.Net.HttpStatusCode.OK;
                response.Reason = "OK";
                response.ContentType = "text/html; charset=utf-8";
                response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");

                using (var fs = System.IO.File.OpenRead(System.IO.File.Exists(html) ? html : htm))
                {
                    response.ContentLength = fs.Length;
                    response.Body = fs;
                    response.Send();
                }

                return true;
            }

            return false;
        }

        private string GetPath(Uri uri)
        {
            if (ForbiddenChars.Any(x => uri.AbsolutePath.Contains(x)))
                throw new BadRequestException("Illegal path");
            var uripath = Uri.UnescapeDataString(uri.AbsolutePath);
            while(uripath.Length > 0 && (uripath.StartsWith("/", StringComparison.Ordinal) || uripath.StartsWith(DirSep, StringComparison.Ordinal)))
                uripath = uripath.Substring(1);
            return System.IO.Path.Combine(m_webroot, uripath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }
    }
}

