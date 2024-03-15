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
using System.Collections.Generic;
using Duplicati.Server.Serialization;
using System.IO;
using Duplicati.Library.RestAPI;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class UISettings : IRESTMethodGET, IRESTMethodPOST, IRESTMethodPATCH
    {
        public void GET(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.OutputOK(FIXMEGlobal.DataConnection.GetUISettingsSchemes());
            }
            else
            {
                info.OutputOK(FIXMEGlobal.DataConnection.GetUISettings(key));
            }
        }

        public void POST(string key, RequestInfo info)
        {
            PATCH(key, info);
        }

        public void PATCH(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.ReportClientError("Scheme is missing", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            IDictionary<string, string> data;
            try
            {
                data = Serializer.Deserialize<Dictionary<string, string>>(new StreamReader(info.Request.Body));
            }
            catch (Exception ex)
            {
                info.ReportClientError(string.Format("Unable to parse settings object: {0}", ex.Message), System.Net.HttpStatusCode.BadRequest);
                return;
            }

            if (data == null)
            {
                info.ReportClientError("Unable to parse settings object", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            if (info.Request.Method == "POST")
                FIXMEGlobal.DataConnection.SetUISettings(key, data);
            else
                FIXMEGlobal.DataConnection.UpdateUISettings(key, data);
            info.OutputOK();
        }

    }
}

