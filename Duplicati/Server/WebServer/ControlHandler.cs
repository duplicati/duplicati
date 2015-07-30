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

        public const string CONTROL_HANDLER_URI = "/control.cgi";

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
            SUPPORTED_METHODS.Add("send-command", SendCommand);
            SUPPORTED_METHODS.Add("get-backup-defaults", GetBackupDefaults);
            SUPPORTED_METHODS.Add("get-folder-contents", GetFolderContents);
            SUPPORTED_METHODS.Add("get-backup", GetBackup);
            SUPPORTED_METHODS.Add("add-backup", AddBackup);
            SUPPORTED_METHODS.Add("update-backup", UpdateBackup);
            SUPPORTED_METHODS.Add("delete-backup", DeleteBackup);
            SUPPORTED_METHODS.Add("copy-backup-to-temp", CopyBackupToTemporary);
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
            SUPPORTED_METHODS.Add("get-server-options", GetServerOptions);
            SUPPORTED_METHODS.Add("set-server-options", SetServerOptions);
        }

        public override bool Process (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            //We use the fake entry point /control.cgi to listen for requests
            //This ensures that the rest of the webserver can just serve plain files
            if (!request.Uri.AbsolutePath.Equals(CONTROL_HANDLER_URI, StringComparison.InvariantCultureIgnoreCase))
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
                using (BodyWriter bw = new BodyWriter(response, request))
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

        private void ReadLogData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            if (string.IsNullOrEmpty(input["id"].Value))
                RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.LogData));
            else
            {
                var key = string.Format("{0}/{1}", input["id"].Value, Duplicati.Library.Utility.Utility.ParseBool(input["remotelog"].Value, false) ? "remotelog" : "log");
                RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
            }

        }

        private void LocateUriDb(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/dbpath", Library.Utility.Uri.UrlPathEncode(input["uri"].Value));
            RESTHandler.DoProcess(request, response, session, "GET", typeof(RESTMethods.RemoteOperation).Name.ToLowerInvariant(), key);
        }

        private void SearchBackupFiles(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.Equals("POST", StringComparison.InvariantCultureIgnoreCase) ? request.Form : request.QueryString;
            var key = string.Format("{0}/files/{1}", input["id"].Value, input["filter"].Value);

            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);

        }

        private void GetFolderContents(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.Filesystem).Name.ToLowerInvariant(), Library.Utility.Uri.UrlPathEncode(request.QueryString["path"].Value));
        }      


        private void GetBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Backup));
        }

        private void DeleteBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = HttpServer.Method.Delete;
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Backup));
        }

        private class AddOrUpdateBackupData
        {
            public Database.Schedule Schedule {get; set;}
            public Database.Backup Backup {get; set;}
        }

        private void UpdateBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = HttpServer.Method.Put;
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Backup));
        }

        private void AddBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Backups));
        }

        private void GetAcknowledgements(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Acknowledgements));
        }

        private void GetChangelog(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Changelog));
        }

        private void GetLicenseData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(Duplicati.License.LicenseReader.ReadLicenses(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Duplicati.Library.Utility.Utility.getEntryAssembly().Location), "licenses")));
        }

        private void RestoreFiles(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/restore", input["id"].Value);
            RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
        }

        private void ListBackupSets(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/filesets", input["id"].Value);
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
        }

        private void ListRemoteFolder(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/list", Library.Utility.Uri.UrlPathEncode(input["url"].Value));
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.RemoteOperation).Name.ToLowerInvariant(), key);
        }

        private void CreateRemoteFolder(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/create", Library.Utility.Uri.UrlPathEncode(input["url"].Value));
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.RemoteOperation).Name.ToLowerInvariant(), key);
        }

        private void TestBackend(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/test", Library.Utility.Uri.UrlPathEncode(input["url"].Value));
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.RemoteOperation).Name.ToLowerInvariant(), key);
        }

        private void ListSystemInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.SystemInfo));
        }

        private void ListSupportedActions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            bw.OutputOK(new { Version = 1, Methods = SUPPORTED_METHODS.Keys });
        }

        private void ListBackups (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Backups));
        }

        private void ListTags(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Tags));
        }

        private void ValidatePath(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/validate", Library.Utility.Uri.UrlPathEncode(input["path"].Value));
            RESTHandler.DoProcess(request, response, session, "GET", typeof(RESTMethods.Filesystem).Name.ToLowerInvariant(), key);
        }

        private void GetProgressState(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.ProgressState));
        }

        private void GetCurrentState (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.ServerState));
        }
            
        private void ListApplicationSettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.SystemWideSettings));
        }

        private void DownloadBugReport(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.BugReport));
        }

        private void DeleteLocalData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/deletedb", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
            RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
        }

        private void GetServerOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.ServerSettings));
        }

        private void SetServerOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = "PATCH";
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.ServerSettings));
        }

        private void CopyBackupToTemporary(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/copytotemp", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
            RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
        }
            
        private void ExportBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var key = string.Format("{0}/export", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
            RESTHandler.DoProcess(request, response, session, request.Method, typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
        }

        private void ImportBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backups).Name.ToLowerInvariant(), "import");
        }

        private void GetNotifications(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Notifications));
        }

        private void DismissNotification(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = "DELETE";
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.Notification));
        }

        private void GetUISettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = "GET";
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.UISettings));
        }

        private void SetUISettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            request.Method = "POST";
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.UISettings));
        }

        private void GetUISettingSchemes(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.UISettings));
        }

        private void GetBackupDefaults(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {   
            RESTHandler.HandleControlCGI(request, response, session, bw, typeof(RESTMethods.BackupDefaults));
        }

        private void PollLogMessages(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            RESTHandler.DoProcess(request, response, session, "GET", typeof(RESTMethods.LogData).Name.ToLowerInvariant(), "poll");
        }

        private void SendCommand(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            string command = input["command"].Value ?? "";

            switch (command.ToLowerInvariant())
            {
                case "check-update":
                    RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Updates).Name.ToLowerInvariant(), "check");
                    return;

                case "install-update":
                    RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Updates).Name.ToLowerInvariant(), "install");
                    return;

                case "activate-update":
                    RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Updates).Name.ToLowerInvariant(), "activate");
                    return;

                case "pause":
                    RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.ServerState).Name.ToLowerInvariant(), "pause");
                    return;

                case "resume":
                    RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.ServerState).Name.ToLowerInvariant(), "resume");
                    return;

                case "stop":
                case "abort":
                    {
                        var key = string.Format("{0}/{1}", input["taskid"].Value, command);
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Task).Name.ToLowerInvariant(), key);
                    }
                    return;

                case "is-backup-active":
                    {
                        var key = string.Format("{0}/isactive", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "GET", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;

                case "run":
                case "run-backup":
                    {
                        var key = string.Format("{0}/start", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;

                case "run-verify":
                    {
                        var key = string.Format("{0}/verify", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;

                case "run-repair-update":
                    {
                        var key = string.Format("{0}/repairupdate", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;

                case "run-repair":
                    {
                        var key = string.Format("{0}/repair", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;
                case "create-report":
                    {
                        var key = string.Format("{0}/createreport", Library.Utility.Uri.UrlPathEncode(input["id"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.Backup).Name.ToLowerInvariant(), key);
                    }
                    return;

                default:
                    {
                        var key = string.Format("{0}", Library.Utility.Uri.UrlPathEncode(input["command"].Value));
                        RESTHandler.DoProcess(request, response, session, "POST", typeof(RESTMethods.WebModule).Name.ToLowerInvariant(), key);
                        return;
                    }
            }
        }

    }
}

