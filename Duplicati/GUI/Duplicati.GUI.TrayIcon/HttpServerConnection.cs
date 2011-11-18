using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Server.Serialization;

namespace Duplicati.GUI.TrayIcon
{
    public class HttpServerConnection
    {
        private Uri m_uri;
        private System.Net.NetworkCredential m_credentials;
        private ServiceState m_serviceState;
        private static readonly System.Text.Encoding ENCODING = System.Text.Encoding.GetEncoding("utf-8");

        public HttpServerConnection(Uri server, System.Net.NetworkCredential credentials)
        {
            m_uri = server;


        }



        private static string EncodeQueryString(Dictionary<string, string> dict)
        {
            return string.Join("&", Array.ConvertAll(dict.Keys.ToArray(), key => string.Format("{0}={1}", Uri.EscapeUriString(key), Uri.EscapeUriString(dict[key]))));
        }

        private string PerformRequest(Dictionary<string, string> queryparams)
        {
            string query = EncodeQueryString(queryparams);
            byte[] data = ENCODING.GetBytes(query);

            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(m_uri);
            req.Method = "POST";
            req.ContentLength = data.Length;
            req.ContentType = "application/x-www-form-urlencoded ; charset=" + ENCODING.BodyName;
            req.Headers.Add("Accept-Charset", ENCODING.BodyName);
            
            using (System.IO.Stream s = req.GetRequestStream())
                s.Write(data, 0, data.Length);

            using(System.Net.HttpWebResponse r = (System.Net.HttpWebResponse)req.GetResponse())
            using (System.IO.Stream s = r.GetResponseStream())
            using(System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                s.CopyTo(ms);
                return ENCODING.GetString(ms.ToArray());
            }
        }

        public ServiceState State
        {
            get { return m_serviceState; }
        }
    }
}
