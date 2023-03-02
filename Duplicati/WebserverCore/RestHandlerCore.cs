//  Copyright (C) 2023, The Duplicati Team
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

using Amazon.S3;
using Duplicati.Library.RestAPI;
using Duplicati.Server.WebServer.RESTMethods;
using Duplicati.WebserverCore;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Bcpg.Sig;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Duplicati.WebserverCore
{
    public class RESTHandlerCoreController : Controller
    {
        public const string API_URI_PATH = "/api/v1";
        public static readonly int API_URI_SEGMENTS = API_URI_PATH.Split(new char[] { '/' }).Length;

        private static readonly Dictionary<string, IRESTMethod> _modules = new Dictionary<string, IRESTMethod>(StringComparer.OrdinalIgnoreCase);

        public static IDictionary<string, IRESTMethod> Modules { get { return _modules; } }

        public RESTHandlerCoreController(IEnumerable<IRESTMethod> restMethods)
        {
            Console.WriteLine("Loaded controller!");
            //FIXME: Why no methods?
            foreach (var method in restMethods)
            {
                var t = method.GetType();
                _modules.Add(t.Name.ToLowerInvariant(), method);
            }
        }

        [Route(API_URI_PATH + "/{module?}/{key?}")]
        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH")]
        public async Task<ActionResult> Index(string? module, string? key)
        {

            module = (module != null && module != "") ? module : "help";

            var method = Request.Method;

            var ci = ParseRequestCulture(Request.Headers.Accept);

            var info = new RequestInfo(new LegacyHttpRequestShim(Request), new LegacyHttpResponseShim(Response), new LegacyHttpSessionShim());

            using (Library.Localization.LocalizationService.TemporaryContext(ci))
            {
                try
                {
                    if (ci != null)
                        Response.Headers.Add("Content-Language", ci.Name);

                    IRESTMethod mod;
                    _modules.TryGetValue(module, out mod);

                    if (mod == null)
                    {
                        return NotFound("No such module");
                    }
                    else if (method == "GET" && mod is IRESTMethodGET get)
                    {
                        //if (info.Request.Form != HttpServer.HttpForm.EmptyForm)
                        //{
                        //    if (info.Request.QueryString == HttpServer.HttpInput.Empty)
                        //    {
                        //        var r = info.Request.GetType().GetField("_queryString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        //        r.SetValue(info.Request, new HttpServer.HttpInput("formdata"));
                        //    }

                        //    foreach (HttpServer.HttpInputItem v in info.Request.Form)
                        //        if (!info.Request.QueryString.Contains(v.Name))
                        //            info.Request.QueryString.Add(v.Name, v.Value);
                        //}

                        get.GET(key, info);
                    }
                    else if (method =="PUT" && mod is IRESTMethodPUT put)
                        put.PUT(key, info);
                    else if (method == "POST" && mod is IRESTMethodPOST post)
                    {
                        //if (info.Request.Form == HttpServer.HttpForm.EmptyForm || info.Request.Form == HttpServer.HttpInput.Empty)
                        //{
                        //    var r = info.Request.GetType().GetMethod("AssignForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new Type[] { typeof(HttpServer.HttpForm) }, null);
                        //    r.Invoke(info.Request, new object[] { new HttpServer.HttpForm(info.Request.QueryString) });
                        //}
                        //else
                        //{
                        //    foreach (HttpServer.HttpInputItem v in info.Request.QueryString)
                        //        if (!info.Request.Form.Contains(v.Name))
                        //            info.Request.Form.Add(v.Name, v.Value);
                        //}

                        post.POST(key, info);
                    }
                    else if (method == "DELETE" && mod is IRESTMethodDELETE delete)
                        delete.DELETE(key, info);
                    else if (method == "PATCH" && mod is IRESTMethodPATCH patch)
                        patch.PATCH(key, info);
                    else
                    {
                        return StatusCode( (int)HttpStatusCode.MethodNotAllowed, "Method is not allowed");
                    }
                }
                catch (Exception ex)
                {
                    FIXMEGlobal.DataConnection.LogError("", string.Format("Request for {0} gave error",Request.Path), ex);
                    Console.WriteLine(ex);

                    try
                    {
                        var wex = ex;
                        while (wex is System.Reflection.TargetInvocationException && wex.InnerException != wex)
                            wex = wex.InnerException;

                        return StatusCode(500, wex.Message
#if DEBUG
                            + "\n" + wex.StackTrace
#endif
                            );
                    }
                    catch (Exception flex)
                    {
                        FIXMEGlobal.DataConnection.LogError("", "Reporting error gave error", flex);
                    }
                }
            }

            return Ok();
        }


        private static readonly ConcurrentDictionary<string, System.Globalization.CultureInfo> _cultureCache = new ConcurrentDictionary<string, System.Globalization.CultureInfo>(StringComparer.OrdinalIgnoreCase);
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
                    opts.Where(y => y.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
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
                .FirstOrDefault(System.Globalization.CultureInfo.InvariantCulture);

        }
    }
    public static class RESTHandlerCoreExtensions
    {
        public static IHostBuilder UseRESTHandlers(
            this IHostBuilder builder)
        {
            var lst =
                from n in typeof(IRESTMethod).Assembly.GetTypes()
                where
                    n.Namespace == typeof(IRESTMethod).Namespace
                    &&
                    typeof(IRESTMethod).IsAssignableFrom(n)
                    &&
                    !n.IsAbstract
                    &&
                    !n.IsInterface
                select n;

            builder.ConfigureServices(services =>
            {
                foreach (var t in lst)
                {
                    services.AddSingleton(t);
                }
            });

            return builder;
        }

        //public static IEndpointRouteBuilder UseRESTHandlerEndpoints(this IEndpointRouteBuilder builder)
        //{
        //    builder.MapFallback(async (HttpContext context) => {
        //        await System.Threading.Tasks.Task.Delay(1);
        //        // context.
        //        //TODO: Register controllers without this middleware ?
        //    });
        //    return builder;
        //}
    }

}

