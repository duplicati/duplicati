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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class RemoteOperation : IRESTMethodGET, IRESTMethodPOST
    {
        private void LocateDbUri(string uri, RequestInfo info)
        {
            var path = Library.Main.DatabaseLocator.GetDatabasePath(uri, null, false, false);
            info.OutputOK(new {
                Exists = !string.IsNullOrWhiteSpace(path),
                Path = path
            });
        }

        private void CreateFolder(string uri, RequestInfo info)
        {
            using(var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(uri, new Dictionary<string, string>()))
                b.CreateFolder();

            info.OutputOK();
        }

        private void UploadFile(string uri, RequestInfo info)
        {
            var data = info.Request.QueryString["data"].Value;
            var remotename = info.Request.QueryString["filename"].Value;

            using(var ms = new System.IO.MemoryStream())   
            using(var b = Library.DynamicLoader.BackendLoader.GetBackend(uri, new Dictionary<string, string>()))
            {
                using(var tf = new Library.Utility.TempFile())
                {
                    System.IO.File.WriteAllText(tf, data);
                    b.PutAsync(remotename, tf, CancellationToken.None).Wait();
                }
            }

            info.OutputOK();
        }

        private void ListFolder(string uri, RequestInfo info)
        {
            using(var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(uri, new Dictionary<string, string>()))
                info.OutputOK(b.List());
        }

        private void TestConnection(string url, RequestInfo info)
        {
            
            var modules = (from n in Library.DynamicLoader.GenericLoader.Modules
                where n is Library.Interface.IConnectionModule
                select n).ToArray();

            try
            {
                var uri = new Library.Utility.Uri(url);
                var qp = uri.QueryParameters;

                var opts = new Dictionary<string, string>();
                foreach (var k in qp.Keys.Cast<string>())
                    opts[k] = qp[k];

                foreach (var n in modules)
                    n.Configure(opts);

                using (var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(url, new Dictionary<string, string>()))
                    b.Test();

                info.OutputOK();
            }
            catch (Duplicati.Library.Interface.FolderMissingException)
            {
                info.ReportServerError("missing-folder");
            }
            catch (Duplicati.Library.Utility.SslCertificateValidator.InvalidCertificateException icex)
            {
                if (string.IsNullOrWhiteSpace(icex.Certificate))
                    info.ReportServerError(icex.Message);
                else
                    info.ReportServerError("incorrect-cert:" + icex.Certificate);
            }
            catch (Duplicati.Library.Utility.HostKeyException hex)
            {
                if (string.IsNullOrWhiteSpace(hex.ReportedHostKey))
                    info.ReportServerError(hex.Message);
                else
                {
                    info.ReportServerError(string.Format(
                        @"incorrect-host-key:""{0}"", accepted-host-key:""{1}""",
                        hex.ReportedHostKey,
                        hex.AcceptedHostKey
                    ));
                }
            }
            finally
            {
                foreach (var n in modules)
                    if (n is IDisposable disposable)
                        disposable.Dispose();
            }
        }

        public void GET(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' }, 2);

            if (parts.Length <= 1)
            {
                info.ReportClientError("No url or operation supplied", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            var url = Library.Utility.Uri.UrlDecode(parts.First());
            var operation = parts.Last().ToLowerInvariant();

            switch (operation)
            {
                case "dbpath":
                    LocateDbUri(url, info);
                    return;
                case "list":
                    ListFolder(url, info);
                    return;
                case "create":
                    CreateFolder(url, info);
                    return;
                case "test":
                    TestConnection(url, info);
                    return;
                default:
                    info.ReportClientError("No such method", System.Net.HttpStatusCode.BadRequest);
                    return;
            }
        }

        public void POST(string key, RequestInfo info)
        {
            string url;

            using(var sr = new System.IO.StreamReader(info.Request.Body, System.Text.Encoding.UTF8, true))
                url = sr.ReadToEnd();

            switch (key)
            {
                case "dbpath":
                    LocateDbUri(url, info);
                    return;
                case "list":
                    ListFolder(url, info);
                    return;
                case "create":
                    CreateFolder(url, info);
                    return;
                case "put":
                    UploadFile(url, info);
                    return;
                case "test":
                    TestConnection(url, info);
                    return;
                default:
                    info.ReportClientError("No such method", System.Net.HttpStatusCode.BadRequest);
                    return;
            }
        }
    }
}

