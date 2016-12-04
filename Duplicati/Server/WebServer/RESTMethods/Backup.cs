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
using Duplicati.Server.Serialization;
using System.IO;
using System.Linq;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Backup : IRESTMethodGET, IRESTMethodPUT, IRESTMethodPOST, IRESTMethodDELETE, IRESTMethodDocumented
    {
        public class GetResponse
        {
            public class GetResponseData
            {
                public Serialization.Interface.ISchedule Schedule;
                public Serialization.Interface.IBackup Backup;
                public Dictionary<string, string> DisplayNames;
            }
            
            public bool success;

            public GetResponseData data;
        }

        private void SearchFiles(IBackup backup, string filterstring, RequestInfo info)
        {
            var filter = filterstring.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
            var timestring = info.Request.QueryString["time"].Value;
            var allversion = Duplicati.Library.Utility.Utility.ParseBool(info.Request.QueryString["all-versions"].Value, false);

            if (string.IsNullOrWhiteSpace(timestring) && !allversion)
            {
                info.ReportClientError("Invalid or missing time");
                return;
            }

            var prefixonly = Duplicati.Library.Utility.Utility.ParseBool(info.Request.QueryString["prefix-only"].Value, false);
            var foldercontents = Duplicati.Library.Utility.Utility.ParseBool(info.Request.QueryString["folder-contents"].Value, false);
            var time = new DateTime();
            if (!allversion)
                time = Duplicati.Library.Utility.Timeparser.ParseTimeInterval(timestring, DateTime.Now);

            var r = Runner.Run(Runner.CreateListTask(backup, filter, prefixonly, allversion, foldercontents, time), false) as Duplicati.Library.Interface.IListResults;

            var result = new Dictionary<string, object>();

            foreach(HttpServer.HttpInputItem n in info.Request.QueryString)
                result[n.Name] = n.Value;

            result["Filesets"] = r.Filesets;
            result["Files"] = r.Files;

            info.OutputOK(result);

        }

        private void ListFileSets(IBackup backup, RequestInfo info)
        {
            var input = info.Request.QueryString;
            var extra = new Dictionary<string, string>();
            extra["list-sets-only"] = "true";
            if (input["include-metadata"].Value != null)
                extra["list-sets-only"] = (!Library.Utility.Utility.ParseBool(input["include-metadata"].Value, false)).ToString();
            if (input["from-remote-only"].Value != null)
                extra["no-local-db"] = Library.Utility.Utility.ParseBool(input["from-remote-only"].Value, false).ToString();

            var r = Runner.Run(Runner.CreateTask(DuplicatiOperation.List, backup, extra), false) as Duplicati.Library.Interface.IListResults;

            if (r.EncryptedFiles && backup.Settings.Any(x => string.Equals("--no-encryption", x.Name, StringComparison.InvariantCultureIgnoreCase)))
                info.ReportServerError("encrypted-storage");
            else
                info.OutputOK(r.Filesets);
        }

        private void FetchLogData(IBackup backup, RequestInfo info)
        {
            using(var con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType))
            {
                con.ConnectionString = "Data Source=" + backup.DBPath;
                con.Open();

                using(var cmd = con.CreateCommand())
                    info.OutputOK(LogData.DumpTable(cmd, "LogData", "ID", info.Request.QueryString["offset"].Value, info.Request.QueryString["pagesize"].Value));
            }
        }

        private void FetchRemoteLogData(IBackup backup, RequestInfo info)
        {
            using(var con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType))
            {
                con.ConnectionString = "Data Source=" + backup.DBPath;
                con.Open();

                using(var cmd = con.CreateCommand())
                {
                    var dt = LogData.DumpTable(cmd, "RemoteOperation", "ID", info.Request.QueryString["offset"].Value, info.Request.QueryString["pagesize"].Value);

                    // Unwrap raw data to a string
                    foreach(var n in dt)
                        try { n["Data"] = System.Text.Encoding.UTF8.GetString((byte[])n["Data"]); }
                    catch { }

                    info.OutputOK(dt);
                }
            }
        }
        private void IsDBUsedElseWhere(IBackup backup, RequestInfo info)
        {
            info.OutputOK(new { inuse = Library.Main.DatabaseLocator.IsDatabasePathInUse(backup.DBPath) });
        }

        private void Export(IBackup backup, RequestInfo info)
        {
            var cmdline = Library.Utility.Utility.ParseBool(info.Request.QueryString["cmdline"].Value, false);
            if (cmdline)
            {
                info.OutputOK(new { Command = Runner.GetCommandLine(Runner.CreateTask(DuplicatiOperation.Backup, backup)) });
            }
            else
            {
                var passphrase = info.Request.QueryString["passphrase"].Value;
                var ipx = Program.DataConnection.PrepareBackupForExport(backup);

                byte[] data;
                using(var ms = new System.IO.MemoryStream())
                using(var sw = new System.IO.StreamWriter(ms))
                {
                    Serializer.SerializeJson(sw, ipx, true);

                    if (!string.IsNullOrWhiteSpace(passphrase))
                    {
                        ms.Position = 0;
                        using(var ms2 = new System.IO.MemoryStream())
                        using(var m = new Duplicati.Library.Encryption.AESEncryption(passphrase, new Dictionary<string, string>()))
                        {
                            m.Encrypt(ms, ms2);
                            data = ms2.ToArray();
                        }
                    }
                    else
                        data = ms.ToArray();
                }

                var filename = Library.Utility.Uri.UrlEncode(backup.Name) + "-duplicati-config.json";
                if (!string.IsNullOrWhiteSpace(passphrase))
                    filename += ".aes";

                info.Response.ContentLength = data.Length;
                info.Response.AddHeader("Content-Disposition", string.Format("attachment; filename={0}", filename));
                info.Response.ContentType = "application/octet-stream";

                info.BodyWriter.SetOK();
                info.Response.SendHeaders();
                info.Response.SendBody(data);
            }
        }

        private void RestoreFiles(IBackup backup, RequestInfo info)
        {
            var input = info.Request.Form;

            string[] filters = parsePaths(input["paths"].Value ?? string.Empty);
            
            var time = Duplicati.Library.Utility.Timeparser.ParseTimeInterval(input["time"].Value, DateTime.Now);
            var restoreTarget = input["restore-path"].Value;
            var overwrite = Duplicati.Library.Utility.Utility.ParseBool(input["overwrite"].Value, false);

            var permissions = Duplicati.Library.Utility.Utility.ParseBool(input["permissions"].Value, false);
            var skip_metadata = Duplicati.Library.Utility.Utility.ParseBool(input["skip-metadata"].Value, false);

            var task = Runner.CreateRestoreTask(backup, filters, time, restoreTarget, overwrite, permissions, skip_metadata);

            Program.WorkThread.AddTask(task);

            info.OutputOK(new { TaskID = task.TaskID });
        }

        private void CreateReport(IBackup backup, RequestInfo info)
        {
            var task = Runner.CreateTask(DuplicatiOperation.CreateReport, backup);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new { Status = "OK", ID = task.TaskID });
        }

        private void ReportRemoteSize(IBackup backup, RequestInfo info)
        {
            var task = Runner.CreateTask(DuplicatiOperation.ListRemote, backup);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new { Status = "OK", ID = task.TaskID });
        }

        private void Repair(IBackup backup, RequestInfo info)
        {
            DoRepair(backup, info, false);
        }

        private void RepairUpdate(IBackup backup, RequestInfo info)
        {
            DoRepair(backup, info, true);
        }

        private void Verify(IBackup backup, RequestInfo info)
        {
            var task = Runner.CreateTask(DuplicatiOperation.Verify, backup);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new {Status = "OK", ID = task.TaskID});
        }

        private void Compact(IBackup backup, RequestInfo info)
        {
            var task = Runner.CreateTask(DuplicatiOperation.Compact, backup);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new { Status = "OK", ID = task.TaskID });
        }

        private string[] parsePaths(string paths)
        {
            string[] filters;
            var rawpaths = (paths ?? string.Empty).Trim();
            
            // We send the file list as a JSON array to avoid encoding issues with the path seperator 
            // as it is an allowed character in file and path names.
            // We also accept the old way, for compatibility with the greeno theme
            if (!string.IsNullOrWhiteSpace(rawpaths) && rawpaths.StartsWith("[", StringComparison.Ordinal) && rawpaths.EndsWith("]", StringComparison.Ordinal))
                filters = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(rawpaths);
            else
                filters = paths.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);

            return filters;
        }

        private void DoRepair(IBackup backup, RequestInfo info, bool repairUpdate)
        {
            var input = info.Request.Form;
            string[] filters = null;
            var extra = new Dictionary<string, string>();
            if (input["only-paths"].Value != null)
                extra["repair-only-paths"] = (Library.Utility.Utility.ParseBool(input["only-paths"].Value, false)).ToString();
            if (input["time"].Value != null)
                extra["time"] = input["time"].Value;
            if (input["version"].Value != null)
                extra["version"] = input["version"].Value;
            if (input["paths"].Value != null)
                filters = parsePaths(input["paths"].Value);

            var task = Runner.CreateTask(repairUpdate ? DuplicatiOperation.RepairUpdate : DuplicatiOperation.Repair, backup, extra, filters);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new {Status = "OK", ID = task.TaskID});
        }

        private void RunBackup(IBackup backup, RequestInfo info)
        {
            var t = Program.WorkThread.CurrentTask;
            var bt = t == null ? null : t.Backup;
            if (bt != null && backup.ID == bt.ID)
            {
                // Already running
            }
            else if (Program.WorkThread.CurrentTasks.Where(x => { 
                var bn = x == null ? null : x.Backup;
                return bn == null || bn.ID == backup.ID;
            }).Any())
            {
                // Already in queue
            }
            else
            {
                Program.WorkThread.AddTask(Runner.CreateTask(DuplicatiOperation.Backup, backup));
                Program.StatusEventNotifyer.SignalNewEvent();
            }

            info.OutputOK();
        }

        private void IsActive(IBackup backup, RequestInfo info)
        {
            var t = Program.WorkThread.CurrentTask;
            var bt = t == null ? null : t.Backup;
            if (bt != null && backup.ID == bt.ID)
            {
                info.OutputOK(new { Status = "OK", Active = true });
                return;
            }
            else if (Program.WorkThread.CurrentTasks.Where(x =>
            { 
                var bn = x == null ? null : x.Backup;
                return bn == null || bn.ID == backup.ID;
            }).Any())
            {
                info.OutputOK(new { Status = "OK", Active = true });
                return;
            }
            else
            {
                info.OutputOK(new { Status = "OK", Active = false });
                return;
            }
        }

        private void UpdateDatabasePath(IBackup backup, RequestInfo info, bool move)
        {
            var np = info.Request.Form["path"].Value;
            if (string.IsNullOrWhiteSpace(np))
                info.ReportClientError("No target path supplied");
            else if (!Path.IsPathRooted(np))
                info.ReportClientError("Target path is relative, please supply a fully qualified path");
            else
            {
                if (move && (File.Exists(np) || Directory.Exists(np)))
                    info.ReportClientError("A file already exists at the new location");
                else
                {
                    if (move)
                        File.Move(backup.DBPath, np);

                    Program.DataConnection.UpdateBackupDBPath(backup, np);
                }
                    
            }
            
        }
            
        public void GET(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' }, 2);
            var bk = Program.DataConnection.GetBackup(parts.First());
            if (bk == null)
                info.ReportClientError("Invalid or missing backup id", System.Net.HttpStatusCode.NotFound);
            else
            {
                if (parts.Length > 1)
                {
                    var operation = parts.Last().Split(new char[] {'/'}).First().ToLowerInvariant();

                    switch (operation)
                    {
                        case "files":
                            var filter = parts.Last().Split(new char[] { '/' }, 2).Skip(1).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(info.Request.QueryString["filter"].Value))
                                filter = info.Request.QueryString["filter"].Value;
                            SearchFiles(bk, filter, info);
                            return;
                        case "log":
                            FetchLogData(bk, info);
                            return;
                        case "remotelog":
                            FetchRemoteLogData(bk, info);
                            return;
                        case "filesets":
                            ListFileSets(bk, info);
                            return;
                        case "export":
                            Export(bk, info);
                            return;
                        case "isdbusedelsewhere":
                            IsDBUsedElseWhere(bk, info);
                            return;
                    case "isactive":
                            IsActive(bk, info);
                            return;
                        default:
                            info.ReportClientError(string.Format("Invalid component: {0}", operation));
                            return;
                    }

                }
                    
                var scheduleId = Program.DataConnection.GetScheduleIDsFromTags(new string[] { "ID=" + bk.ID });
                var schedule = scheduleId.Any() ? Program.DataConnection.GetSchedule(scheduleId.First()) : null;
                var sourcenames = SpecialFolders.GetSourceNames(bk);

                //TODO: Filter out the password in both settings and the target url

                info.OutputOK(new GetResponse()
                {
                    success = true,
                    data = new GetResponse.GetResponseData() {
                        Schedule = schedule,
                        Backup = bk,
                        DisplayNames = sourcenames
                    }
                });
            }
        }

        public void POST(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' }, 2);
            var bk = Program.DataConnection.GetBackup(parts.First());
            if (bk == null)
                info.ReportClientError("Invalid or missing backup id");
            else
            {
                if (parts.Length > 1)
                {
                    var operation = parts.Last().Split(new char[] { '/' }).First().ToLowerInvariant();

                    switch (operation)
                    {
                        case "deletedb":
                            System.IO.File.Delete(bk.DBPath);
                            info.OutputOK();
                            return;

                        case "movedb":     
                            UpdateDatabasePath(bk, info, true);
                            return;

                        case "updatedb":
                            UpdateDatabasePath(bk, info, false);
                            return;

                        case "restore":
                            RestoreFiles(bk, info);
                            return;

                        case "createreport":
                            CreateReport(bk, info);
                            return;

                        case "repair":
                            Repair(bk, info);
                            return;

                        case "repairupdate":
                            RepairUpdate(bk, info);
                            return;

                        case "verify":
                            Verify(bk, info);
                            return;

                        case "compact":
                            Compact(bk, info);
                            return;

                        case "start":
                        case "run":
                            RunBackup(bk, info);
                            return;

                        case "report-remote-size":
                            ReportRemoteSize(bk, info);
                            return;

                        case "copytotemp":
                            var ipx = Serializer.Deserialize<Database.Backup>(new StringReader(Newtonsoft.Json.JsonConvert.SerializeObject(bk)));

                            using(var tf = new Duplicati.Library.Utility.TempFile())
                                ipx.DBPath = tf;
                            ipx.ID = null;

                            info.OutputOK(new { status = "OK", ID = Program.DataConnection.RegisterTemporaryBackup(ipx) });
                            return;
                    }
                }

                info.ReportClientError("Invalid request");
            }
        }
            
        public void PUT(string key, RequestInfo info)
        {
            string str = info.Request.Form["data"].Value;
            if (string.IsNullOrWhiteSpace(str))
                str = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8).ReadToEnd();

            if (string.IsNullOrWhiteSpace(str))
            {
                info.ReportClientError("Missing backup object");
                return;
            }

            Backups.AddOrUpdateBackupData data = null;
            try
            {
                data = Serializer.Deserialize<Backups.AddOrUpdateBackupData>(new StringReader(str));
                if (data.Backup == null)
                {
                    info.ReportClientError("Data object had no backup entry");
                    return;
                }

                if (!string.IsNullOrEmpty(key))
                    data.Backup.ID = key;

                if (string.IsNullOrEmpty(data.Backup.ID))
                {
                    info.ReportClientError("Invalid or missing backup id");
                    return;
                }          


                if (data.Backup.IsTemporary)
                {
                    var backup = Program.DataConnection.GetBackup(data.Backup.ID);
                    if (backup.IsTemporary)
                        throw new InvalidDataException("External is temporary but internal is not?");

                    Program.DataConnection.UpdateTemporaryBackup(backup);
                    info.OutputOK();
                }
                else
                {                    
                    lock(Program.DataConnection.m_lock)
                    {
                        var backup = Program.DataConnection.GetBackup(data.Backup.ID);
                        if (backup == null)
                        {
                            info.ReportClientError("Invalid or missing backup id");
                            return;
                        }

                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase) && x.ID != data.Backup.ID).Any())
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

                        //TODO: Merge in real passwords where the placeholder is found
                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);

                    }

                    info.OutputOK();
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                    info.ReportClientError(string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                else
                    info.ReportClientError(string.Format("Unable to save backup or schedule: {0}", ex.Message));
            }
        }

        public void DELETE(string key, RequestInfo info)
        {
            var backup = Program.DataConnection.GetBackup(key);
            if (backup == null)
            {
                info.ReportClientError("Invalid or missing backup id");
                return;
            }

            var delete_remote_files = Library.Utility.Utility.ParseBool(info.Request.Param["delete-remote-files"].Value, false);

            if (delete_remote_files)
            {
                var captcha_token = info.Request.Param["captcha-token"].Value;
                var captcha_answer = info.Request.Param["captcha-answer"].Value;
                if (string.IsNullOrWhiteSpace(captcha_token) || string.IsNullOrWhiteSpace(captcha_answer))
                {
                    info.ReportClientError("Missing captcha");
                    return;
                }

                if (!Captcha.SolvedCaptcha(captcha_token, "DELETE /backup/" + backup.ID, captcha_answer))
                {
                    info.ReportClientError("Invalid captcha", System.Net.HttpStatusCode.Forbidden);
                    return;
                }
            }

            if (Program.WorkThread.Active)
            {
                try
                {
                    //TODO: It's not safe to access the values like this, 
                    //because the runner thread might interfere
                    var nt = Program.WorkThread.CurrentTask;
                    if (backup.Equals(nt == null ? null : nt.Backup))
                    {
                        bool force;
                        if (!bool.TryParse(info.Request.QueryString["force"].Value, out force))
                            force = false;

                        if (!force)
                        {
                            info.OutputError(new { status = "failed", reason = "backup-in-progress" });
                            return;
                        }

                        bool hasPaused = Program.LiveControl.State == LiveControls.LiveControlState.Paused;
                        Program.LiveControl.Pause();

                        try
                        {
                            for(int i = 0; i < 10; i++)
                                if (Program.WorkThread.Active)
                                {
                                    var t = Program.WorkThread.CurrentTask;
                                    if (backup.Equals(t == null ? null : t.Backup))
                                        System.Threading.Thread.Sleep(1000);
                                    else
                                        break;
                                }
                                else
                                    break;
                        }
                        finally
                        {
                        }

                        if (Program.WorkThread.Active)
                        {
                            var t = Program.WorkThread.CurrentTask;
                            if (backup.Equals(t == null ? null : t.Backup))
                            {
                                if (hasPaused)
                                    Program.LiveControl.Resume();
                                info.OutputError(new { status = "failed", reason = "backup-unstoppable" });
                                return;
                            }
                        }

                        if (hasPaused)
                            Program.LiveControl.Resume();
                    }
                }
                catch (Exception ex)
                {
                    info.OutputError(new { status = "error", message = ex.Message });
                    return;
                }
            }

            var extra = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(info.Request.Param["delete-local-db"].Value))
                extra["delete-local-db"] = info.Request.Param["delete-local-db"].Value;
            if (delete_remote_files)
                extra["delete-remote-files"] = "true";

            var task = Runner.CreateTask(DuplicatiOperation.Delete, backup, extra);
            Program.WorkThread.AddTask(task);
            Program.StatusEventNotifyer.SignalNewEvent();

            info.OutputOK(new { Status = "OK", ID = task.TaskID });
        }
        public string Description { get { return "Retrieves, updates or deletes an existing backup and schedule"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(GetResponse)),
                    new KeyValuePair<string, Type>(HttpServer.Method.Put, typeof(Backups.AddOrUpdateBackupData)),
                    new KeyValuePair<string, Type>(HttpServer.Method.Delete, typeof(long))
                };
            }
        }
    }
}

