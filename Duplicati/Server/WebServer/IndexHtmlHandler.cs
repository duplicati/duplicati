//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using HttpServer;
using HttpServer.HttpModules;
using HttpServer.Exceptions;

namespace Duplicati.Server.WebServer
{
    internal class IndexHtmlHandler : HttpModule
    {
        private readonly string m_webroot;

        private static readonly string[] ForbiddenChars = new string[] {"\\", "..", ":"}.Union(from n in System.IO.Path.GetInvalidPathChars() select n.ToString()).Distinct().ToArray();
        private static readonly string DirSep = System.IO.Path.DirectorySeparatorChar.ToString();

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

