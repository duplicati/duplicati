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
using Duplicati.Library.Common.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class BackupDefaults : IRESTMethodGET
    {
        
		private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<BackupDefaults>();

        public void GET(string key, RequestInfo info)
        {
            // Start with a scratch object
            var o = new Newtonsoft.Json.Linq.JObject();

            // Add application wide settings
            o.Add("ApplicationOptions", new Newtonsoft.Json.Linq.JArray(
                from n in FIXMEGlobal.DataConnection.Settings
                select Newtonsoft.Json.Linq.JObject.FromObject(n)
            ));

            try
            {
                // Add built-in defaults
                Newtonsoft.Json.Linq.JObject n;
                using(var s = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(FIXMEGlobal), "newbackup.json")))
                    n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(s.ReadToEnd());

                MergeJsonObjects(o, n);
            }
            catch (Exception e)
            {
                Library.Logging.Log.WriteErrorMessage(LOGTAG, "BackupDefaultsError", e, "Failed to locate embeded backup defaults");
            }

            try
            {
                // Add install defaults/overrides, if present
                var path = SystemIO.IO_OS.PathCombine(Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR, "newbackup.json");
                if (System.IO.File.Exists(path))
                {
                    Newtonsoft.Json.Linq.JObject n;
                    n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(System.IO.File.ReadAllText(path));

                    MergeJsonObjects(o, n);
                }
            }
            catch (Exception e)
            {
                Library.Logging.Log.WriteErrorMessage(LOGTAG, "BackupDefaultsError", e, "Failed to process newbackup.json");
            }

            info.OutputOK(new
            {
                success = true,
                data = o
            });            
        }

        private static void MergeJsonObjects(Newtonsoft.Json.Linq.JObject self, Newtonsoft.Json.Linq.JObject other)
        {
            foreach(var p in other.Properties())
            {
                var sp = self.Property(p.Name);
                if (sp == null)
                    self.Add(p);
                else
                {
                    switch (p.Type)
                    {
                        // Primitives override
                        case Newtonsoft.Json.Linq.JTokenType.Boolean:
                        case Newtonsoft.Json.Linq.JTokenType.Bytes:
                        case Newtonsoft.Json.Linq.JTokenType.Comment:
                        case Newtonsoft.Json.Linq.JTokenType.Constructor:
                        case Newtonsoft.Json.Linq.JTokenType.Date:
                        case Newtonsoft.Json.Linq.JTokenType.Float:
                        case Newtonsoft.Json.Linq.JTokenType.Guid:
                        case Newtonsoft.Json.Linq.JTokenType.Integer:
                        case Newtonsoft.Json.Linq.JTokenType.String:
                        case Newtonsoft.Json.Linq.JTokenType.TimeSpan:
                        case Newtonsoft.Json.Linq.JTokenType.Uri:
                        case Newtonsoft.Json.Linq.JTokenType.None:
                        case Newtonsoft.Json.Linq.JTokenType.Null:
                        case Newtonsoft.Json.Linq.JTokenType.Undefined:
                            self.Replace(p);
                            break;

                            // Arrays merge
                        case Newtonsoft.Json.Linq.JTokenType.Array:
                            if (sp.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                sp.Value = new Newtonsoft.Json.Linq.JArray(((Newtonsoft.Json.Linq.JArray)sp.Value).Union((Newtonsoft.Json.Linq.JArray)p.Value));
                            else
                            {
                                var a = new Newtonsoft.Json.Linq.JArray(sp.Value);
                                sp.Value = new Newtonsoft.Json.Linq.JArray(a.Union((Newtonsoft.Json.Linq.JArray)p.Value));
                            }

                            break;

                            // Objects merge
                        case Newtonsoft.Json.Linq.JTokenType.Object:
                            if (sp.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                                MergeJsonObjects((Newtonsoft.Json.Linq.JObject)sp.Value, (Newtonsoft.Json.Linq.JObject)p.Value);
                            else
                                sp.Value = p.Value;
                            break;

                            // Ignore other stuff                                
                        default:
                            break;
                    }
                }
            }
        }
    }
}

