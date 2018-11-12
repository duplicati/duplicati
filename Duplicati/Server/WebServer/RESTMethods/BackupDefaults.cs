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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class BackupDefaults : IRESTMethodGET
    {
        public void GET(string key, RequestInfo info)
        {
            // Start with a scratch object
            var o = new Newtonsoft.Json.Linq.JObject();

            // Add application wide settings
            o.Add("ApplicationOptions", new Newtonsoft.Json.Linq.JArray(
                from n in Program.DataConnection.Settings
                select Newtonsoft.Json.Linq.JObject.FromObject(n)
            ));

            try
            {
                // Add built-in defaults
                Newtonsoft.Json.Linq.JObject n;
                using(var s = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".newbackup.json")))
                    n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(s.ReadToEnd());

                MergeJsonObjects(o, n);
            }
            catch
            {
            }

            try
            {
                // Add install defaults/overrides, if present
                var path = SystemIO.IO_OS.PathCombine(Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "newbackup.json");
                if (System.IO.File.Exists(path))
                {
                    Newtonsoft.Json.Linq.JObject n;
                    n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(System.IO.File.ReadAllText(path));

                    MergeJsonObjects(o, n);
                }
            }
            catch
            {
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

