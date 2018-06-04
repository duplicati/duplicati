//  Copyright (C) 2016, The Duplicati Team
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
using System.Collections.Generic;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Captcha : IRESTMethodGET, IRESTMethodPOST
    {
        private class CaptchaEntry
        {
            public readonly string Answer;
            public readonly string Target;
            public int Attempts;
            public readonly DateTime Expires;

            public CaptchaEntry(string answer, string target)
            {
                Answer = answer;
                Target = target;
                Attempts = 4;
                Expires = DateTime.Now.AddMinutes(2);
            }
        }

        private static readonly object m_lock = new object();
        private static readonly Dictionary<string, CaptchaEntry> m_captchas = new Dictionary<string, CaptchaEntry>();

        public static bool SolvedCaptcha(string token, string target, string answer)
        {
            lock(m_lock)
            {
                CaptchaEntry tp;
                m_captchas.TryGetValue(token ?? string.Empty, out tp);
                if (tp == null)
                    return false;
                
                if (tp.Attempts > 0)
                    tp.Attempts--;
                
                return tp.Attempts >= 0 && string.Equals(tp.Answer, answer, StringComparison.OrdinalIgnoreCase) && tp.Target == target && tp.Expires >= DateTime.Now;
            }
        }

        public void GET(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.ReportClientError("Missing token value", System.Net.HttpStatusCode.Unauthorized);
                return;
            }
            else
            {
                string answer = null;
                lock (m_lock)
                {
                    CaptchaEntry tp;
                    m_captchas.TryGetValue(key, out tp);
                    if (tp != null && tp.Expires > DateTime.Now)
                        answer = tp.Answer;
                }

                if (string.IsNullOrWhiteSpace(answer))
                {
                    info.ReportClientError("No such entry", System.Net.HttpStatusCode.NotFound);
                    return;
                }

                using (var bmp = CaptchaUtil.CreateCaptcha(answer))
                using (var ms = new System.IO.MemoryStream())
                {
                    info.Response.ContentType = "image/jpeg";
                    info.Response.ContentLength = ms.Length;
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    ms.Position = 0;

                    info.Response.ContentType = "image/jpeg";
                    info.Response.ContentLength = ms.Length;
                    info.Response.SendHeaders();
                    ms.CopyTo(info.Response.Body);
                    info.Response.Send();
                }
            }        
        }

        public void POST(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                var target = info.Request.Param["target"].Value;
                if (string.IsNullOrWhiteSpace(target))
                {
                    info.ReportClientError("Missing target parameter", System.Net.HttpStatusCode.BadRequest);
                    return;
                }

                var answer = CaptchaUtil.CreateRandomAnswer(minlength: 6, maxlength: 6);
                var nonce = Guid.NewGuid().ToString();

                string token;
                using (var ms = new System.IO.MemoryStream())
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(answer + nonce);
                    ms.Write(bytes, 0, bytes.Length);
                    ms.Position = 0;
                    token = Library.Utility.Utility.Base64PlainToBase64Url(Library.Utility.Utility.CalculateHash(ms));
                }

                lock (m_lock)
                {
                    var expired = m_captchas.Where(x => x.Value.Expires < DateTime.Now).Select(x => x.Key).ToArray();
                    foreach (var x in expired)
                        m_captchas.Remove(x);

                    if (m_captchas.Count > 3)
                    {
                        info.ReportClientError("Too many captchas, wait 2 minutes and try again", System.Net.HttpStatusCode.ServiceUnavailable);
                        return;
                    }

                    m_captchas[token] = new CaptchaEntry(answer, target);
                }

                info.OutputOK(new
                {
                    token = token
                });
            }
            else
            {
                var answer = info.Request.Param["answer"].Value;
                var target = info.Request.Param["target"].Value;
                if (string.IsNullOrWhiteSpace(answer))
                {
                    info.ReportClientError("Missing answer parameter", System.Net.HttpStatusCode.BadRequest);
                    return;
                }
                if (string.IsNullOrWhiteSpace(target))
                {
                    info.ReportClientError("Missing target parameter", System.Net.HttpStatusCode.BadRequest);
                    return;
                }

                if (SolvedCaptcha(key, target, answer))
                    info.OutputOK();
                else
                    info.ReportClientError("Incorrect", System.Net.HttpStatusCode.Forbidden);
            }
        }
    }
}
