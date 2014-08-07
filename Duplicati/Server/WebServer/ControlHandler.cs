//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using HttpServer;
using HttpServer.HttpModules;
using System.Collections.Generic;
using Duplicati.Server.Serialization;
using System.IO;

namespace Duplicati.Server.WebServer
{
    internal partial class ControlHandler : HttpModule
    {
        private delegate void ProcessSub(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter writer);
        private readonly Dictionary<string, ProcessSub> SUPPORTED_METHODS;

        public ControlHandler()
        {
            SUPPORTED_METHODS = new Dictionary<string, ProcessSub>(System.StringComparer.InvariantCultureIgnoreCase);

            //Make a list of all supported actions
            SUPPORTED_METHODS.Add("supported-actions", ListSupportedActions);
            SUPPORTED_METHODS.Add("system-info", ListSystemInfo);
            SUPPORTED_METHODS.Add("list-backups", ListBackups);
            SUPPORTED_METHODS.Add("get-current-state", GetCurrentState);
            SUPPORTED_METHODS.Add("get-progress-state", GetProgressState);
            SUPPORTED_METHODS.Add("list-application-settings", ListApplicationSettings);
            SUPPORTED_METHODS.Add("list-options", ListCoreOptions);
            SUPPORTED_METHODS.Add("send-command", SendCommand);
            SUPPORTED_METHODS.Add("get-backup-defaults", GetBackupDefaults);
            SUPPORTED_METHODS.Add("get-folder-contents", GetFolderContents);
            SUPPORTED_METHODS.Add("get-backup", GetBackup);
            SUPPORTED_METHODS.Add("add-backup", AddBackup);
            SUPPORTED_METHODS.Add("update-backup", UpdateBackup);
            SUPPORTED_METHODS.Add("delete-backup", DeleteBackup);
            SUPPORTED_METHODS.Add("validate-path", ValidatePath);
            SUPPORTED_METHODS.Add("list-tags", ListTags);
            SUPPORTED_METHODS.Add("test-backend", TestBackend);
            SUPPORTED_METHODS.Add("create-remote-folder", CreateRemoteFolder);
            SUPPORTED_METHODS.Add("list-remote-folder", ListRemoteFolder);
            SUPPORTED_METHODS.Add("list-backup-sets", ListBackupSets);
            SUPPORTED_METHODS.Add("search-backup-files", SearchBackupFiles);
            SUPPORTED_METHODS.Add("restore-files", RestoreFiles);
            SUPPORTED_METHODS.Add("read-log", ReadLogData);
            SUPPORTED_METHODS.Add("get-license-data", GetLicenseData);
            SUPPORTED_METHODS.Add("get-changelog", GetChangelog);
            SUPPORTED_METHODS.Add("get-acknowledgements", GetAcknowledgements);
            SUPPORTED_METHODS.Add("locate-uri-db", LocateUriDb);
            SUPPORTED_METHODS.Add("poll-log-messages", PollLogMessages);
            SUPPORTED_METHODS.Add("export-backup", ExportBackup);
            SUPPORTED_METHODS.Add("import-backup", ImportBackup);
            SUPPORTED_METHODS.Add("get-ui-settings", GetUISettings);
            SUPPORTED_METHODS.Add("set-ui-settings", SetUISettings);
            SUPPORTED_METHODS.Add("get-ui-schemes", GetUISettingSchemes);
            SUPPORTED_METHODS.Add("get-notifications", GetNotifications);
            SUPPORTED_METHODS.Add("dismiss-notification", DismissNotification);
            SUPPORTED_METHODS.Add("download-bug-report", DownloadBugReport);
            SUPPORTED_METHODS.Add("delete-local-data", DeleteLocalData);
        }

        public override bool Process (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            //We use the fake entry point /control.cgi to listen for requests
            //This ensures that the rest of the webserver can just serve plain files
            if (!request.Uri.AbsolutePath.Equals("/control.cgi", StringComparison.InvariantCultureIgnoreCase))
                return false;

            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            string action = input["action"].Value ?? "";

            //Lookup the actual handler method
            ProcessSub method;
            SUPPORTED_METHODS.TryGetValue(action, out method);

            if (method == null) {
                response.Status = System.Net.HttpStatusCode.NotImplemented;
                response.Reason = "Unsupported action: " + (action == null ? "<null>" : "");
                response.Send();
            } else {
                //Default setup
                response.Status = System.Net.HttpStatusCode.OK;
                response.Reason = "OK";
                #if DEBUG
                response.ContentType = "text/plain";
                #else
                response.ContentType = "text/json";
                #endif
                using (BodyWriter bw = new BodyWriter(response))
                {
                    try
                    {
                        method(request, response, session, bw);
                    }
                    catch (Exception ex)
                    {
                        Program.DataConnection.LogError("", string.Format("Request for {0} gave error", action), ex);
                        Console.WriteLine(ex.ToString());

                        try
                        {
                            if (!response.HeadersSent)
                            {
                                response.Status = System.Net.HttpStatusCode.InternalServerError;
                                response.Reason = "Error";
                                response.ContentType = "text/plain";

                                bw.WriteJsonObject(new
                                {
                                    Message = ex.Message,
                                    Type = ex.GetType().Name,
                                    #if DEBUG
                                    Stacktrace = ex.ToString()
                                    #endif
                                });
                                bw.Flush();
                            }
                        }
                        catch (Exception flex)
                        {
                            Program.DataConnection.LogError("", "Reporting error gave error", flex);
                        }
                    }
                }
            }

            return true;
        }

        private void ReportError(HttpServer.IHttpResponse response, BodyWriter bw, string message)
        {
            response.Status = System.Net.HttpStatusCode.InternalServerError;
            response.Reason = message;

            bw.WriteJsonObject(new { Error = message });
        }

        private List<Dictionary<string, object>> DumpTable(System.Data.IDbCommand cmd, string tablename, string pagingfield, string offset_str, string pagesize_str)
        {
            var result = new List<Dictionary<string, object>>();

            long pagesize;
            if (!long.TryParse(pagesize_str, out pagesize))
                pagesize = 100;

            pagesize = Math.Max(10, Math.Min(500, pagesize));

            cmd.CommandText = "SELECT * FROM \"" + tablename + "\"";
            long offset = 0;
            if (!string.IsNullOrWhiteSpace(offset_str) && long.TryParse(offset_str, out offset) && !string.IsNullOrEmpty(pagingfield))
            {
                var p = cmd.CreateParameter();
                p.Value = offset;
                cmd.Parameters.Add(p);

                cmd.CommandText += " WHERE \"" + pagingfield + "\" < ?";
            }

            if (!string.IsNullOrEmpty(pagingfield))
                cmd.CommandText += " ORDER BY \"" + pagingfield + "\" DESC";
            cmd.CommandText += " LIMIT " + pagesize.ToString();

            using(var rd = cmd.ExecuteReader())
            {
                var names = new List<string>();
                for(var i = 0; i < rd.FieldCount; i++)
                    names.Add(rd.GetName(i));

                while (rd.Read())
                {
                    var dict = new Dictionary<string, object>();
                    for(int i = 0; i < names.Count; i++)
                        dict[names[i]] = rd.GetValue(i);

                    result.Add(dict);                                    
                }
            }

            return result;
        }

        private void LocateUriDb(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var uri = input["uri"].Value;

            if (string.IsNullOrWhiteSpace(uri))
            {
                ReportError(response, bw, "Invalid request, missing uri");
            }
            else
            {
                var path = Library.Main.DatabaseLocator.GetDatabasePath(uri, null, false, false);

                bw.SetOK();
                bw.WriteJsonObject(new {Exists = !string.IsNullOrWhiteSpace(path)});
            }
        }

        private void GetAcknowledgements(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "acknowledgements.txt");
                bw.SetOK();
                bw.WriteJsonObject(new {
                    Status = "OK",
                    Acknowledgements = System.IO.File.ReadAllText(path)
                });            
        }

        private void GetChangelog(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var fromUpdate = input["from-update"].Value;

            if (!Library.Utility.Utility.ParseBool(fromUpdate, false))
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "changelog.txt");
                bw.SetOK();
                bw.WriteJsonObject(new {
                    Status = "OK",
                    Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    Changelog = System.IO.File.ReadAllText(path)
                });
            }
            else
            {
                var updateInfo = Program.DataConnection.ApplicationSettings.UpdatedVersion;
                if (updateInfo == null)
                {
                    ReportError(response, bw, "No update found");
                }
                else
                {
                    bw.SetOK();
                    bw.WriteJsonObject(new {
                        Status = "OK",
                        Version = updateInfo.Version,
                        Changelog = updateInfo.ChangeInfo
                    });
                }
            }
        }

        private void GetLicenseData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(Duplicati.License.LicenseReader.ReadLicenses(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "licenses")));
        }

        private void RestoreFiles(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }

            var filters = input["paths"].Value.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
            var time = Duplicati.Library.Utility.Timeparser.ParseTimeInterval(input["time"].Value, DateTime.Now);
            var restoreTarget = input["restore-path"].Value;
            var overwrite = Duplicati.Library.Utility.Utility.ParseBool(input["overwrite"].Value, false);
            var task = Runner.CreateRestoreTask(bk, filters, time, restoreTarget, overwrite);
            Program.WorkThread.AddTask(task);

            bw.OutputOK(new { TaskID = task.TaskID });

        }

        private void ListBackupSets(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }


            var r = Runner.Run(Runner.CreateTask(DuplicatiOperation.List, bk), false) as Duplicati.Library.Interface.IListResults;

            bw.OutputOK(r.Filesets);
        }

        private void ListRemoteFolder(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (input["url"] == null || input["url"].Value == null)
            {
                ReportError(response, bw, "The url parameter was not set");
                return;
            }

            try
            {
                using(var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(input["url"].Value, new Dictionary<string, string>()))
                    bw.OutputOK(new { Status = "OK", Folders = b.List() });

            }
            catch (Exception ex)
            {
                ReportError(response, bw, ex.Message);
            }
        }
        private void CreateRemoteFolder(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (input["url"] == null || input["url"].Value == null)
            {
                ReportError(response, bw, "The url parameter was not set");
                return;
            }

            try
            {
                using(var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(input["url"].Value, new Dictionary<string, string>()))
                    b.CreateFolder();

                bw.OutputOK();
            }
            catch (Exception ex)
            {
                ReportError(response, bw, ex.Message);
            }
        }

        private void TestBackend(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (input["url"] == null || input["url"].Value == null)
            {
                ReportError(response, bw, "The url parameter was not set");
                return;
            }

            var modules = (from n in Library.DynamicLoader.GenericLoader.Modules
                                    where n is Library.Interface.IConnectionModule
                                    select n).ToArray();

            try
            {
                var url = input["url"].Value;
                var uri = new Library.Utility.Uri(url);
                var qp = uri.QueryParameters;

                var opts = new Dictionary<string, string>();
                foreach(var k in qp.Keys.Cast<string>())
                    opts[k] = qp[k];

                foreach(var n in modules)
                    n.Configure(opts);

                using(var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(url, new Dictionary<string, string>()))
                    b.Test();

                bw.OutputOK();
            }
            catch (Duplicati.Library.Interface.FolderMissingException)
            {
                ReportError(response, bw, "missing-folder");
            }
            catch (Duplicati.Library.Utility.SslCertificateValidator.InvalidCertificateException icex)
            {
                if (string.IsNullOrWhiteSpace(icex.Certificate))
                    ReportError(response, bw, icex.Message);
                else
                    ReportError(response, bw, "incorrect-cert:" + icex.Certificate);
            }
            catch (Exception ex)
            {
                ReportError(response, bw, ex.Message);
            }
            finally
            {
                foreach(var n in modules)
                    if (n is IDisposable)
                        ((IDisposable)n).Dispose();
            }
        }

        private void ListSystemInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(new
            {
                APIVersion = 1,
                PasswordPlaceholder = Duplicati.Server.WebServer.Server.PASSWORD_PLACEHOLDER,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                ServerVersionName = Duplicati.License.VersionNumbers.Version,
                ServerTime = DateTime.Now,
                OSType = Library.Utility.Utility.IsClientLinux ? (Library.Utility.Utility.IsClientOSX ? "OSX" : "Linux") : "Windows",
                DirectorySeparator = System.IO.Path.DirectorySeparatorChar,
                PathSeparator = System.IO.Path.PathSeparator,
                CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
                MonoVersion = Duplicati.Library.Utility.Utility.IsMono ? Duplicati.Library.Utility.Utility.MonoVersion.ToString() : null,
                MachineName = System.Environment.MachineName,
                NewLine = System.Environment.NewLine,
                CLRVersion = System.Environment.Version.ToString(),
                CLROSInfo = new
                {
                    Platform = System.Environment.OSVersion.Platform.ToString(),
                    ServicePack = System.Environment.OSVersion.ServicePack,
                    Version = System.Environment.OSVersion.Version.ToString(),
                    VersionString = System.Environment.OSVersion.VersionString
                },
                Options = Serializable.ServerSettings.Options,
                CompressionModules =  Serializable.ServerSettings.CompressionModules,
                EncryptionModules = Serializable.ServerSettings.EncryptionModules,
                BackendModules = Serializable.ServerSettings.BackendModules,
                GenericModules = Serializable.ServerSettings.GenericModules,
                WebModules = Serializable.ServerSettings.WebModules,
                ConnectionModules = Serializable.ServerSettings.ConnectionModules,
                UsingAlternateUpdateURLs = Duplicati.Library.AutoUpdater.AutoUpdateSettings.UsesAlternateURLs,
                LogLevels = Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))
            });
        }

        private void ListSupportedActions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(new { Version = 1, Methods = SUPPORTED_METHODS.Keys });
        }

        private void ListBackups (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
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

            bw.OutputOK(all.ToArray());
        }

        private void ListTags(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var r = 
                from n in 
                Serializable.ServerSettings.CompressionModules
                    .Union(Serializable.ServerSettings.EncryptionModules)
                    .Union(Serializable.ServerSettings.BackendModules)
                    .Union(Serializable.ServerSettings.GenericModules)
                    select n.Key.ToLower();

            // Append all known tags
            r = r.Union(from n in Program.DataConnection.Backups select n.Tags into p from x in p select x.ToLower());
            bw.OutputOK(r);
        }

        private void ValidatePath(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (input["path"] == null || input["path"].Value == null)
            {
                ReportError(response, bw, "The path parameter was not set");
                return;
            }

            try
            {
                string path = SpecialFolders.ExpandEnvironmentVariables(input["path"].Value);                
                if (System.IO.Path.IsPathRooted(path) && System.IO.Directory.Exists(path))
                {
                    bw.OutputOK();
                    return;
                }
            }
            catch
            {
            }

            ReportError(response, bw, "File or folder not found");
            return;
        }

        private bool LongPollCheck(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, BodyWriter bw, EventPollNotify poller, ref long id, out bool isError)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (Library.Utility.Utility.ParseBool(input["longpoll"].Value, false))
            {
                long lastEventId;
                if (!long.TryParse(input["lasteventid"].Value, out lastEventId))
                {
                    ReportError(response, bw, "When activating long poll, the request must include the last event id");
                    isError = true;
                    return false;
                }

                TimeSpan ts;
                try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                catch (Exception ex)
                {
                    ReportError(response, bw, "Invalid duration: " + ex.Message);
                    isError = true;
                    return false;
                }

                if (ts <= TimeSpan.FromSeconds(10) || ts.TotalMilliseconds > int.MaxValue)
                {
                    ReportError(response, bw, "Invalid duration, must be at least 10 seconds, and less than " + int.MaxValue + " milliseconds");
                    isError = true;
                    return false;
                }

                isError = false;
                id = poller.Wait(lastEventId, (int)ts.TotalMilliseconds);
                return true;
            }

            isError = false;
            return false;
        }

        private void GetProgressState(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            if (Program.GenerateProgressState == null)
            {
                ReportError(response, bw, "No active backup");
            }
            else
            {
                var ev = Program.GenerateProgressState();
                bw.OutputOK(ev);
            }
        }

        private void GetCurrentState (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bool isError;
            long id = 0;
            if (LongPollCheck(request, response, bw, Program.StatusEventNotifyer, ref id, out isError))
            {
                //Make sure we do not report a higher number than the eventnotifyer says
                var st = new Serializable.ServerStatus();
                st.LastEventID = id;
                bw.OutputOK(st);
            }
            else if (!isError)
            {
                bw.OutputOK(new Serializable.ServerStatus());
            }
        }

        private void ListCoreOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(new Duplicati.Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);
        }

        private void ListApplicationSettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(Program.DataConnection.ApplicationSettings);
        }

        private void DownloadBugReport(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            long id;
            long.TryParse(input["id"].Value, out id);

            var tf = Program.DataConnection.GetTempFiles().Where(x => x.ID == id).FirstOrDefault();
            if (tf == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }

            if (!System.IO.File.Exists(tf.Path))
            {
                ReportError(response, bw, "File is missing");
                return;
            }

            var filename = "bugreport.zip";
            using(var fs = System.IO.File.OpenRead(tf.Path))
            {
                response.ContentLength = fs.Length;
                response.AddHeader("Content-Disposition", string.Format("attachment; filename={0}", filename));
                response.ContentType = "application/octet-stream";

                bw.SetOK();
                response.SendHeaders();
                fs.CopyTo(response.Body);
                response.Send();
            }
        }

        private void DeleteLocalData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }

            System.IO.File.Delete(bk.DBPath);
            bw.OutputOK();
        }

        private class ImportExportStructure
        {
            public string CreatedByVersion { get; set; }
            public Duplicati.Server.Database.Schedule Schedule { get; set; }
            public Duplicati.Server.Database.Backup Backup { get; set; }
            public Dictionary<string, string> DisplayNames { get; set; }
        }

        private void ExportBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }

            var cmdline = Library.Utility.Utility.ParseBool(input["cmdline"].Value, false);
            if (cmdline)
            {
                bw.OutputOK(new { Command = Runner.GetCommandLine(Runner.CreateTask(DuplicatiOperation.Backup, bk)) });
            }
            else
            {
                var passphrase = input["passphrase"].Value;
                var scheduleId = Program.DataConnection.GetScheduleIDsFromTags(new string[] { "ID=" + bk.ID });

                var ipx = new ImportExportStructure() {
                    CreatedByVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    Backup = (Database.Backup)bk,
                    Schedule = (Database.Schedule)(scheduleId.Any() ? Program.DataConnection.GetSchedule(scheduleId.First()) : null),
                    DisplayNames = GetSourceNames(bk)
                };

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

                var filename = Library.Utility.Uri.UrlEncode(bk.Name) + "-duplicati-config.json";
                if (!string.IsNullOrWhiteSpace(passphrase))
                    filename += ".aes";

                response.ContentLength = data.Length;
                response.AddHeader("Content-Disposition", string.Format("attachment; filename={0}", filename));
                response.ContentType = "application/octet-stream";

                bw.SetOK();
                response.SendHeaders();
                response.SendBody(data);
            }
        }

        private void ImportBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var output_template = "<html><body><script type=\"text/javascript\">var rp = null; try { rp = parent['CBM']; } catch (e) {}; rp = rp || alert; rp('MSG');</script></body></html>";
            //output_template = "<html><body><script type=\"text/javascript\">alert('MSG');</script></body></html>";
            try
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                var cmdline = Library.Utility.Utility.ParseBool(input["cmdline"].Value, false);
                output_template = output_template.Replace("CBM", input["callback"].Value);
                if (cmdline)
                {
                    response.ContentType = "text/html";
                    bw.Write(output_template.Replace("MSG", "Import from commandline not yet implemented"));
                }
                else
                {
                    ImportExportStructure ipx;

                    var file = request.Form.GetFile("config");
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
                                ipx = Serializer.Deserialize<ImportExportStructure>(sr);
                        }
                        else
                        {
                            using(var sr = new System.IO.StreamReader(fs))
                                ipx = Serializer.Deserialize<ImportExportStructure>(sr);
                        }
                    }

                    ipx.Backup.ID = null;
                    ((Database.Backup)ipx.Backup).DBPath = null;

                    if (ipx.Schedule != null)
                        ipx.Schedule.ID = -1;

                    lock(Program.DataConnection.m_lock)
                    {
                        var basename = ipx.Backup.Name;
                        var c = 0;
                        while (c++ < 100 && Program.DataConnection.Backups.Where(x => x.Name.Equals(ipx.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                            ipx.Backup.Name = basename + " (" + c.ToString() + ")"; 
                        
                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(ipx.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                        {
                            bw.SetOK();
                            response.ContentType = "text/html";
                            bw.Write(output_template.Replace("MSG", "There already exists a backup with the name: " + basename.Replace("\'", "\\'")));
                        }

                        Program.DataConnection.AddOrUpdateBackupAndSchedule(ipx.Backup, ipx.Schedule);
                    }

                    response.ContentType = "text/html";
                    bw.Write(output_template.Replace("MSG", "OK"));

                    //bw.OutputOK(new { status = "OK", ID = ipx.Backup.ID });
                }
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError("", "Failed to import backup", ex);
                response.ContentType = "text/html";
                bw.Write(output_template.Replace("MSG", ex.Message.Replace("\'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n")));
            }

        }

        private void GetNotifications(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(Program.DataConnection.GetNotifications());
        }

        private void DismissNotification(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var str = input["id"].Value;
            long id;
            long.TryParse(str, out id);

            if (Program.DataConnection.DismissNotification(id))
                bw.OutputOK();
            else
                ReportError(response, bw, "No such notification");

        }

        private void GetUISettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var scheme = input["scheme"].Value;
            if (string.IsNullOrWhiteSpace(scheme))
            {
                ReportError(response, bw, "No scheme supplied");
                return;
            }

            bw.OutputOK(Program.DataConnection.GetUISettings(scheme));
        }

        private void SetUISettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var scheme = input["scheme"].Value;
            var str = input["data"].Value;
            if (string.IsNullOrWhiteSpace(scheme))
            {
                ReportError(response, bw, "No scheme supplied");
                return;
            }

            if (string.IsNullOrWhiteSpace(str))
            {
                ReportError(response, bw, "No data supplied");
                return;
            }

            IDictionary<string, string> data;
            try
            {
                data = Serializer.Deserialize<Dictionary<string, string>>(new StringReader(str));
            }
            catch (Exception ex)
            {
                ReportError(response, bw, string.Format("Unable to parse settings object: {0}", ex.Message));
                return;
            }

            if (data == null)
            {
                ReportError(response, bw, string.Format("Unable to parse settings object"));
                return;
            }

            Program.DataConnection.SetUISettings(scheme, data);
            bw.OutputOK();
        }

        private void GetUISettingSchemes(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(Program.DataConnection.GetUISettingsSchemes());
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

        private void GetBackupDefaults(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {   
            // Start with a scratch object
            var o = new Newtonsoft.Json.Linq.JObject();

            // Add application wide settings
            o.Add("ApplicationOptions", new Newtonsoft.Json.Linq.JArray(Program.DataConnection.Settings));

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
                var path = System.IO.Path.Combine(Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "newbackup.json");
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

            bw.OutputOK(new
            {
                success = true,
                data = o
            });
        }

        private void PollLogMessages(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var level_str = input["level"].Value ?? "";
            var id_str = input["id"].Value ?? "";

            Library.Logging.LogMessageType level;
            long id;

            long.TryParse(id_str, out id);
            Enum.TryParse(level_str, true, out level);

            bw.OutputOK(Program.LogHandler.AfterID(id, level));
        }

        private void SendCommand(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            string command = input["command"].Value ?? "";

            switch (command.ToLowerInvariant())
            {
                case "check-update":
                    Program.UpdatePoller.CheckNow();
                    bw.OutputOK();
                    return;

                case "install-update":
                    Program.UpdatePoller.InstallUpdate();
                    bw.OutputOK();
                    return;

                case "activate-update":
                    if (Program.WorkThread.CurrentTask != null || Program.WorkThread.CurrentTasks.Count != 0)
                    {
                        ReportError(response, bw, "Cannot activate update while task is running or scheduled");
                    }
                    else
                    {
                        Program.UpdatePoller.ActivateUpdate();
                        bw.OutputOK();
                    }
                    return;

                case "pause":
                    if (input.Contains("duration") && !string.IsNullOrWhiteSpace(input["duration"].Value))
                    {
                        TimeSpan ts;
                        try
                        {
                            ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value);
                        }
                        catch (Exception ex)
                        {
                            ReportError(response, bw, ex.Message);
                            return;
                        }
                        if (ts.TotalMilliseconds > 0)
                            Program.LiveControl.Pause(ts);
                        else
                            Program.LiveControl.Pause();
                    }
                    else
                    {
                        Program.LiveControl.Pause();
                    }

                    bw.OutputOK();
                    return;
                case "resume":
                    Program.LiveControl.Resume();
                    bw.OutputOK();
                    return;

                case "stop":
                case "abort":
                    {
                        var task = Program.WorkThread.CurrentTask;
                        var tasks = Program.WorkThread.CurrentTasks;
                        long taskid;
                        if (!input.Contains("taskid") || !long.TryParse(input["taskid"].Value ?? "", out taskid))
                        {
                            ReportError(response, bw, "Invalid or missing taskid");
                            return;
                        }

                        if (task != null)
                            tasks.Insert(0, task);

                        task = tasks.Where(x => x.TaskID == taskid).FirstOrDefault();
                        if (task == null)
                        {
                            ReportError(response, bw, "No such task");
                            return;
                        }

                        if (string.Equals(command, "abort", StringComparison.InvariantCultureIgnoreCase))
                            task.Abort();
                        else
                            task.Stop();

                        bw.OutputOK();
                        return;
                    }

                case "is-backup-active":
                    {
                        var backup = Program.DataConnection.GetBackup(input["id"].Value);
                        if (backup == null)
                        {
                            ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                            return;
                        }

                        var t = Program.WorkThread.CurrentTask;
                        var bt = t == null ? null : t.Backup;
                        if (bt != null && backup.ID == bt.ID)
                        {
                            bw.OutputOK(new { Status = "OK", Active = true });
                            return;
                        }
                        else if (Program.WorkThread.CurrentTasks.Where(x =>
                        { 
                            var bn = x == null ? null : x.Backup;
                            return bn == null || bn.ID == backup.ID;
                        }).Any())
                        {
                            bw.OutputOK(new { Status = "OK", Active = true });
                            return;
                        }
                        else
                        {
                            bw.OutputOK(new { Status = "OK", Active = false });
                            return;
                        }
                    }

                case "run":
                case "run-backup":
                    {

                        var backup = Program.DataConnection.GetBackup(input["id"].Value);
                        if (backup == null)
                        {
                            ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                            return;
                        }

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
                    }
                    bw.OutputOK();
                    return;

                case "run-verify":
                    {
                        var backup = Program.DataConnection.GetBackup(input["id"].Value);
                        if (backup == null)
                        {
                            ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                            return;
                        }

                        Program.WorkThread.AddTask(Runner.CreateTask(DuplicatiOperation.Verify, backup));
                        Program.StatusEventNotifyer.SignalNewEvent();
                    }
                    bw.OutputOK();
                    return;

                case "run-repair":
                    {
                        var backup = Program.DataConnection.GetBackup(input["id"].Value);
                        if (backup == null)
                        {
                            ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                            return;
                        }

                        Program.WorkThread.AddTask(Runner.CreateTask(DuplicatiOperation.Repair, backup));
                        Program.StatusEventNotifyer.SignalNewEvent();
                    }
                    bw.OutputOK();
                    return;
                case "create-report":
                    {
                        var backup = Program.DataConnection.GetBackup(input["id"].Value);
                        if (backup == null)
                        {
                            ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                            return;
                        }

                        Program.WorkThread.AddTask(Runner.CreateTask(DuplicatiOperation.CreateReport, backup));
                        Program.StatusEventNotifyer.SignalNewEvent();
                    }
                    bw.OutputOK();
                    return;

                default:

                    var m = Duplicati.Library.DynamicLoader.WebLoader.Modules.Where(x => x.Key.Equals(command, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                    if (m == null)
                    {
                        ReportError(response, bw, string.Format("Unsupported command {0}", command));
                        return;
                    }

                    bw.OutputOK(new { 
                        Status = "OK", 
                        Result = m.Execute(input.Where(x => 
                            !x.Name.Equals("command", StringComparison.InvariantCultureIgnoreCase)
                            &&
                            !x.Name.Equals("action", StringComparison.InvariantCultureIgnoreCase)
                        ).ToDictionary(x => x.Name, x => x.Value))
                    });
                    return;
            }
        }

    }
}

