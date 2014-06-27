//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using HttpServer;
using HttpServer.HttpModules;

namespace Duplicati.Server.WebServer
{
    internal class IndexHtmlHandler : HttpModule
    {
        private string m_defaultdoc;

        public IndexHtmlHandler(string defaultdoc) { m_defaultdoc = defaultdoc; }

        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            if ((request.Uri.AbsolutePath == "/" || request.Uri.AbsolutePath == "/index.html" || request.Uri.AbsolutePath == "/index.htm") && System.IO.File.Exists(m_defaultdoc))
            {
                response.Status = System.Net.HttpStatusCode.OK;
                response.Reason = "OK";
                response.ContentType = "text/html";

                using (var fs = System.IO.File.OpenRead(m_defaultdoc))
                {
                    response.ContentLength = fs.Length;
                    response.Body = fs;
                    response.Send();
                }

                return true;
            }

            return false;
        }
    }
}

