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
using System.Collections.Generic;
using Duplicati.Server.Serialization;
using System.IO;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Backups : IRESTMethodGET, IRESTMethodPOST, IRESTMethodDocumented
    {
        public class AddOrUpdateBackupData
        {
            public Database.Schedule Schedule { get; set;}
            public Database.Backup Backup { get; set;}
        }

        public void GET(string key, RequestInfo info)
        {
            var schedules = Program.DataConnection.Schedules;
            var backups = Program.DataConnection.Backups;

            var all = from n in backups
                select new AddOrUpdateBackupData() {
                Backup = (Database.Backup)n,
                Schedule = 
                    (from x in schedules
                        where x.Tags != null && x.Tags.Contains("ID=" + n.ID)
                        select (Database.Schedule)x).FirstOrDefault()
                };

            info.BodyWriter.OutputOK(all.ToArray());
        }

        private void ImportBackup(RequestInfo info)
        {
            var output_template = "<html><body><script type=\"text/javascript\">var jso = 'JSO'; var rp = null; try { rp = parent['CBM']; } catch (e) {}; if (rp) { rp('MSG', jso); } else { alert; rp('MSG'); };</script></body></html>";
            //output_template = "<html><body><script type=\"text/javascript\">alert('MSG');</script></body></html>";
            try
            {
                var input = info.Request.Form;
                var cmdline = Library.Utility.Utility.ParseBool(input["cmdline"].Value, false);
                var direct = Library.Utility.Utility.ParseBool(input["direct"].Value, false);
                output_template = output_template.Replace("CBM", input["callback"].Value);
                if (cmdline)
                {
                    info.Response.ContentType = "text/html";
                    info.BodyWriter.Write(output_template.Replace("MSG", "Import from commandline not yet implemented"));
                }
                else
                {
                    Serializable.ImportExportStructure ipx;

                    var file = info.Request.Form.GetFile("config");
                    if (file == null)
                        throw new Exception("No file uploaded");

                    var buf = new byte[3];
                    using(var fs = System.IO.File.OpenRead(file.Filename))
                    {
                        fs.Read(buf, 0, buf.Length);

                        fs.Position = 0;
                        if (buf[0] == 'A' && buf[1] == 'E' && buf[2] == 'S')
                        {
                            var passphrase = input["passphrase"].Value;
                            using(var m = new Duplicati.Library.Encryption.AESEncryption(passphrase, new Dictionary<string, string>()))
                            using(var m2 = m.Decrypt(fs))
                            using(var sr = new System.IO.StreamReader(m2))
                                ipx = Serializer.Deserialize<Serializable.ImportExportStructure>(sr);
                        }
                        else
                        {
                            using(var sr = new System.IO.StreamReader(fs))
                                ipx = Serializer.Deserialize<Serializable.ImportExportStructure>(sr);
                        }
                    }

                    ipx.Backup.ID = null;
                    ((Database.Backup)ipx.Backup).DBPath = null;

                    if (ipx.Schedule != null)
                        ipx.Schedule.ID = -1;

                    if (direct)
                    {
                        lock (Program.DataConnection.m_lock)
                        {
                            var basename = ipx.Backup.Name;
                            var c = 0;
                            while (c++ < 100 && Program.DataConnection.Backups.Where(x => x.Name.Equals(ipx.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                                ipx.Backup.Name = basename + " (" + c.ToString() + ")";

                            if (Program.DataConnection.Backups.Where(x => x.Name.Equals(ipx.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                            {
                                info.BodyWriter.SetOK();
                                info.Response.ContentType = "text/html";
                                info.BodyWriter.Write(output_template.Replace("MSG", "There already exists a backup with the name: " + basename.Replace("\'", "\\'")));
                            }

                            var err = Program.DataConnection.ValidateBackup(ipx.Backup, ipx.Schedule);
                            if (!string.IsNullOrWhiteSpace(err))
                            {
                                info.ReportClientError(err);
                                return;
                            }

                            Program.DataConnection.AddOrUpdateBackupAndSchedule(ipx.Backup, ipx.Schedule);
                        }

                        info.Response.ContentType = "text/html";
                        info.BodyWriter.Write(output_template.Replace("MSG", "OK"));

                    }
                    else
                    {
                        using (var sw = new StringWriter())
                        {
                            Serializer.SerializeJson(sw, ipx, true);
                            output_template = output_template.Replace("'JSO'", sw.ToString());
                        }
                        info.BodyWriter.Write(output_template.Replace("MSG", "Import completed, but a browser issue prevents loading the contents. Try using the direct import method instead."));
                    }
                }
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError("", "Failed to import backup", ex);
                info.Response.ContentType = "text/html";
                info.BodyWriter.Write(output_template.Replace("MSG", ex.Message.Replace("\'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n")));
            }            
        }

        public void POST(string key, RequestInfo info)
        {
            if ("import".Equals(key, StringComparison.InvariantCultureIgnoreCase))
            {
                ImportBackup(info);
                return;
            }

            AddOrUpdateBackupData data = null;
            try
            {
                var str = info.Request.Form["data"].Value;
                if (string.IsNullOrWhiteSpace(str))
                    str = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8).ReadToEnd();

                data = Serializer.Deserialize<AddOrUpdateBackupData>(new StringReader(str));
                if (data.Backup == null)
                {
                    info.ReportClientError("Data object had no backup entry");
                    return;
                }

                data.Backup.ID = null;

                if (Duplicati.Library.Utility.Utility.ParseBool(info.Request.Form["temporary"].Value, false))
                {
                    using(var tf = new Duplicati.Library.Utility.TempFile())
                        data.Backup.DBPath = tf;

                    data.Backup.Filters = data.Backup.Filters ?? new Duplicati.Server.Serialization.Interface.IFilter[0];
                    data.Backup.Settings = data.Backup.Settings ?? new Duplicati.Server.Serialization.Interface.ISetting[0];

                    Program.DataConnection.RegisterTemporaryBackup(data.Backup);

                    info.OutputOK(new { status = "OK", ID = data.Backup.ID });
                }
                else
                {
                    if (Library.Utility.Utility.ParseBool(info.Request.Form["existing_db"].Value, false))
                    {
                        data.Backup.DBPath = Library.Main.DatabaseLocator.GetDatabasePath(data.Backup.TargetURL, null, false, false);
                        if (string.IsNullOrWhiteSpace(data.Backup.DBPath))
                            throw new Exception("Unable to find remote db path?");
                    }


                    lock(Program.DataConnection.m_lock)
                    {
                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                        {
                            info.ReportClientError("There already exists a backup with the name: " + data.Backup.Name);
                            return;
                        }

                        var err = Program.DataConnection.ValidateBackup(data.Backup, data.Schedule);
                        if (!string.IsNullOrWhiteSpace(err))
                        {
                            info.ReportClientError(err);
                            return;
                        }

                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);
                    }

                    info.OutputOK(new { status = "OK", ID = data.Backup.ID });
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                    info.ReportClientError(string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                else
                    info.ReportClientError(string.Format("Unable to save schedule or backup object: {0}", ex.Message));
            }
        }


        public string Description { get { return "Return a list of current backups and their schedules"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(AddOrUpdateBackupData[])),
                    new KeyValuePair<string, Type>(HttpServer.Method.Post, typeof(AddOrUpdateBackupData))
                };
            }
        }
    }
}

