using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpServer.MVC;
using HttpServer.HttpModules;

namespace Duplicati.Server
{
    public class WebServer
    {
        private HttpServer.HttpServer m_server;

        public WebServer(int port)
        {
            m_server = new HttpServer.HttpServer();

            m_server.Add(new DynamicHandler());

            FileModule fh = new FileModule("/", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "webroot"));
            fh.AddDefaultMimeTypes();
            m_server.Add(fh);

            m_server.Start(System.Net.IPAddress.Any, port);
        }

        private class BodyWriter : System.IO.StreamWriter, IDisposable
        {
            private HttpServer.IHttpResponse m_resp;
            public BodyWriter(HttpServer.IHttpResponse resp)
                : base(resp.Body, resp.Encoding)
            {
                m_resp = resp;
            }

            protected override void Dispose(bool disposing)
            {
                base.Flush();
                m_resp.ContentLength = base.BaseStream.Length;
                m_resp.Send();

                base.Dispose(disposing);
            }
        }

        private class DynamicHandler : HttpModule
        {
            private delegate void ProcessSub(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session);
            
            private readonly Dictionary<string, ProcessSub> SUPPORTED_METHODS;

            public DynamicHandler()
            {
                SUPPORTED_METHODS = new Dictionary<string, ProcessSub>(System.StringComparer.InvariantCultureIgnoreCase);

                SUPPORTED_METHODS.Add("list-schedules", ListSchedules);
                SUPPORTED_METHODS.Add("get-current-state", GetCurrentState);
            }


            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                if (!request.Uri.AbsolutePath.Equals("/control.cgi", StringComparison.InvariantCultureIgnoreCase))
                    return false;

                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                string action = input["action"].Value ?? "";

                ProcessSub method;
                SUPPORTED_METHODS.TryGetValue(action, out method);

                if (method == null)
                {
                    response.Status = System.Net.HttpStatusCode.NotImplemented;
                    response.Reason = "Unsupported action: " + (action == null ? "<null>" : "");
                    response.Send();
                }
                else
                {
                    //Default setup
                    response.Status = System.Net.HttpStatusCode.OK;
                    response.Reason = "OK";
                    //response.ContentType = "text/json";
                    response.ContentType = "text/plain";

                    method(request, response, session);
                }

                return true;
            }

            private void ListSchedules(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                using (var b = new BodyWriter(response))
                {
                    LitJson.JsonWriter w = new LitJson.JsonWriter(b);
                    w.WriteArrayStart();

                    foreach (Datamodel.Schedule s in Program.DataConnection.GetObjects<Datamodel.Schedule>())
                    {
                        w.WriteObjectStart();

                        w.WritePropertyName("name");
                        w.Write(s.Name);
                        w.WritePropertyName("id");
                        w.Write(s.ID);

                        w.WriteObjectEnd();
                    }

                    w.WriteArrayEnd();
                }
            }

            private void GetCurrentState(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                using (var b = new BodyWriter(response))
                {
                    LitJson.JsonWriter w = new LitJson.JsonWriter(b);

                    w.WriteObjectStart();

                    w.WritePropertyName("eventid");
                    w.Write(Program.Events.CurrentEventId);

                    w.WritePropertyName("paused");
                    w.Write(Program.LiveControl.State.ToString());

                    w.WritePropertyName("pause-timeout");
                    w.Write(0);



                    w.WriteObjectEnd();
                }
            }

        }
    }
}
