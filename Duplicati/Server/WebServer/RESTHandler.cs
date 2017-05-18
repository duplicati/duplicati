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
using HttpServer.HttpModules;
using System.Collections.Generic;
using Duplicati.Server.WebServer.RESTMethods;

namespace Duplicati.Server.WebServer
{
    public class RESTHandler : HttpModule
    {
        public const string API_URI_PATH = "/api/v1";
        public static readonly int API_URI_SEGMENTS = API_URI_PATH.Split(new char[] {'/'}).Length;

        private static readonly Dictionary<string, IRESTMethod> _modules = new Dictionary<string, IRESTMethod>(StringComparer.InvariantCultureIgnoreCase);

        public static IDictionary<string, IRESTMethod> Modules { get { return _modules; } }

        /// <summary>
        /// Loads all REST modules in the Duplicati.Server.WebServer.RESTMethods namespace
        /// </summary>
        static RESTHandler()
        {
            var lst = 
                from n in typeof(RESTHandler).Assembly.GetTypes()
                where
                    n.Namespace == typeof(IRESTMethod).Namespace
                    &&
                    typeof(IRESTMethod).IsAssignableFrom(n)
                    &&
                    !n.IsAbstract
                    &&
                    !n.IsInterface
                select n;

            foreach(var t in lst)
            {
                var m = (IRESTMethod)Activator.CreateInstance(t);
                _modules.Add(t.Name.ToLowerInvariant(), m);
            }
        }

        public static void HandleControlCGI(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw, Type module)
        {
            var method = request.Method;
            if (!string.IsNullOrWhiteSpace(request.Headers["X-HTTP-Method-Override"]))
                method = request.Headers["X-HTTP-Method-Override"];
            
            DoProcess(request, response, session, method, module.Name.ToLowerInvariant(), (request.Method.ToUpper() == "POST" ? request.Form : request.QueryString)["id"].Value);
        }

        private static Dictionary<string, System.Globalization.CultureInfo> _cultureCache = new Dictionary<string, System.Globalization.CultureInfo>(StringComparer.OrdinalIgnoreCase);

        private static System.Globalization.CultureInfo ParseRequestCulture(RequestInfo info)
        {
            // Inject the override
            return ParseRequestCulture(string.Format("{0},{1}", info.Request.Headers["X-UI-Language"], info.Request.Headers["Accept-Language"]));
        }

        public static System.Globalization.CultureInfo ParseDefaultRequestCulture(RequestInfo info)
        {
            if (info == null)
                return null;
            return ParseRequestCulture(info.Request.Headers["Accept-Language"]);
        }

        private static System.Globalization.CultureInfo ParseRequestCulture(string acceptheader)
        {
            acceptheader = acceptheader ?? string.Empty;

            // Lock-free read
            System.Globalization.CultureInfo ci;
            if (_cultureCache.TryGetValue(acceptheader, out ci))
                return ci;

            // Lock-free assignment, we might compute the value twice
            return _cultureCache[acceptheader] =
                // Parse headers like "Accept-Language: da, en-gb;q=0.8, en;q=0.7"
                acceptheader
                .Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x =>
                {
                    var opts = x.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                    var lang = opts.FirstOrDefault();
                    var weight =
                    opts.Where(y => y.StartsWith("q=", StringComparison.InvariantCultureIgnoreCase))
                        .Select(y =>
                        {
                            float f;
                            float.TryParse(y.Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f);
                            return f;
                        }).FirstOrDefault();

                    // Set the default weight=1
                    if (weight <= 0.001 && weight >= 0)
                        weight = 1;

                    return new KeyValuePair<string, float>(lang, weight);
                })
                // Handle priority
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)
                .Distinct()
                // Filter invalid/unsupported items
                .Where(x => !string.IsNullOrWhiteSpace(x) && Library.Localization.LocalizationService.ParseCulture(x) != null)
                .Select(x => Library.Localization.LocalizationService.ParseCulture(x))
                // And get the first that works
                .FirstOrDefault();

        }

        public static void DoProcess(RequestInfo info, string method, string module, string key)
        {
            var ci = ParseRequestCulture(info);

            using ( Library.Localization.LocalizationService.TemporaryContext(ci))
            try
            {
                if (ci != null)
                    info.Response.AddHeader("Content-Language", ci.Name);

                IRESTMethod mod;
                _modules.TryGetValue(module, out mod);

                if (mod == null)
                {
                    info.Response.Status = System.Net.HttpStatusCode.NotFound;
                    info.Response.Reason = "No such module";
                }
                else if (method == HttpServer.Method.Get && mod is IRESTMethodGET)
                {
                    if (info.Request.Form != HttpServer.HttpForm.EmptyForm)
                    {
                        if (info.Request.QueryString == HttpServer.HttpInput.Empty)
                        {
                            var r = info.Request.GetType().GetField("_queryString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            r.SetValue(info.Request, new HttpServer.HttpInput("formdata"));
                        }

                        foreach(HttpServer.HttpInputItem v in info.Request.Form)
                            if (!info.Request.QueryString.Contains(v.Name))
                                info.Request.QueryString.Add(v.Name, v.Value);
                    }
                    ((IRESTMethodGET)mod).GET(key, info);
                }
                else if (method == HttpServer.Method.Put && mod is IRESTMethodPUT)
                    ((IRESTMethodPUT)mod).PUT(key, info);
                else if (method == HttpServer.Method.Post && mod is IRESTMethodPOST)
                {
                    if (info.Request.Form == HttpServer.HttpForm.EmptyForm)
                    {
                        var r = info.Request.GetType().GetMethod("AssignForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new Type[] { typeof(HttpServer.HttpForm) }, null);
                        r.Invoke(info.Request, new object[] { new HttpServer.HttpForm(info.Request.QueryString) });
                    }
                    else
                    {
                        foreach(HttpServer.HttpInputItem v in info.Request.QueryString)
                            if (!info.Request.Form.Contains(v.Name))
                                info.Request.Form.Add(v.Name, v.Value);
                    }
                    ((IRESTMethodPOST)mod).POST(key, info);
                }
                else if (method == HttpServer.Method.Delete && mod is IRESTMethodDELETE)
                    ((IRESTMethodDELETE)mod).DELETE(key, info);
                else if (method == "PATCH" && mod is IRESTMethodPATCH)
                    ((IRESTMethodPATCH)mod).PATCH(key, info);
                else
                {
                    info.Response.Status = System.Net.HttpStatusCode.MethodNotAllowed;
                    info.Response.Reason = "Method is not allowed";
                }
            }
            catch(Exception ex)
            {
                Program.DataConnection.LogError("", string.Format("Request for {0} gave error", info.Request.Uri), ex);
                Console.WriteLine(ex.ToString());

                try
                {
                    if (!info.Response.HeadersSent)
                    {
                        info.Response.Status = System.Net.HttpStatusCode.InternalServerError;
                        info.Response.Reason = "Error";
                        info.Response.ContentType = "text/plain";

                        var wex = ex;
                        while (wex is System.Reflection.TargetInvocationException && wex.InnerException != wex)
                            wex = wex.InnerException;
                            

                        info.BodyWriter.WriteJsonObject(new
                        {
                            Message = ex.Message,
                            Type = ex.GetType().Name,
                            #if DEBUG
                            Stacktrace = ex.ToString()
                            #endif
                        });
                        info.BodyWriter.Flush();
                    }
                }
                catch (Exception flex)
                {
                    Program.DataConnection.LogError("", "Reporting error gave error", flex);
                }
            }
        }

        public static void DoProcess(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, string method, string module, string key)
        {
            using(var reqinfo = new RequestInfo(request, response, session))
                DoProcess(reqinfo, method, module, key);
        }
            
        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            if (!request.Uri.AbsolutePath.StartsWith(API_URI_PATH, StringComparison.InvariantCultureIgnoreCase))
                return false;

            var module = request.Uri.Segments.Skip(API_URI_SEGMENTS).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(module))
                module = "help";

            module = module.Trim('/');

            var key = string.Join("", request.Uri.Segments.Skip(API_URI_SEGMENTS + 1)).Trim('/');

            var method = request.Method;
            if (!string.IsNullOrWhiteSpace(request.Headers["X-HTTP-Method-Override"]))
                method = request.Headers["X-HTTP-Method-Override"];

            DoProcess(request, response, session, method, module, key);

            return true;
        }
    }
}

