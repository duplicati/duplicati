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
using Duplicati.Server.Serialization;
using System.IO;

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private class AddOrUpdateBackupData
        {
            public Database.Schedule Schedule {get; set;}
            public Database.Backup Backup {get; set;}
        }

        private void UpdateBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            string str = request.Form["data"].Value;
            if (string.IsNullOrWhiteSpace(str))
            {
                ReportError(response, bw, "Missing backup object");
                return;
            }

            AddOrUpdateBackupData data = null;
            try
            {
                data = Serializer.Deserialize<AddOrUpdateBackupData>(new StringReader(str));
                if (data.Backup == null)
                {
                    ReportError(response, bw, "Data object had no backup entry");
                    return;
                }

                if (data.Backup.ID == null)
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }                    

                if (data.Backup.IsTemporary)
                {
                    var backup = Program.DataConnection.GetBackup(data.Backup.ID);
                    if (backup.IsTemporary)
                        throw new InvalidDataException("External is temporary but internal is not?");

                    Program.DataConnection.UpdateTemporaryBackup(backup);
                    bw.OutputOK();
                }
                else
                {                    
                    lock(Program.DataConnection.m_lock)
                    {
                        var backup = Program.DataConnection.GetBackup(data.Backup.ID);
                        if (backup == null)
                        {
                            ReportError(response, bw, "Invalid or missing backup id");
                            return;
                        }

                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase) && x.ID != data.Backup.ID).Any())
                        {
                            ReportError(response, bw, "There already exists a backup with the name: " + data.Backup.Name);
                            return;
                        }

                        //TODO: Merge in real passwords where the placeholder is found
                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);

                    }

                    bw.OutputOK();
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                    ReportError(response, bw, string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                else
                    ReportError(response, bw, string.Format("Unable to save backup or schedule: {0}", ex.Message));

            }
        }

        private void AddBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            var str = request.Form["data"].Value;

            if (string.IsNullOrWhiteSpace(str))
            {
                ReportError(response, bw, "Missing backup object");
                return;
            }

            AddOrUpdateBackupData data = null;
            try
            {
                data = Serializer.Deserialize<AddOrUpdateBackupData>(new StringReader(str));
                if (data.Backup == null)
                {
                    ReportError(response, bw, "Data object had no backup entry");
                    return;
                }

                data.Backup.ID = null;

                if (Duplicati.Library.Utility.Utility.ParseBool(request.Form["temporary"].Value, false))
                {
                    using(var tf = new Duplicati.Library.Utility.TempFile())
                        data.Backup.DBPath = tf;

                    Program.DataConnection.RegisterTemporaryBackup(data.Backup);

                    bw.OutputOK(new { status = "OK", ID = data.Backup.ID });
                }
                else
                {
                    if (Library.Utility.Utility.ParseBool(request.Form["existing_db"].Value, false))
                    {
                        data.Backup.DBPath = Library.Main.DatabaseLocator.GetDatabasePath(data.Backup.TargetURL, null, false, false);
                        if (string.IsNullOrWhiteSpace(data.Backup.DBPath))
                            throw new Exception("Unable to find remote db path?");
                    }


                    lock(Program.DataConnection.m_lock)
                    {
                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                        {
                            ReportError(response, bw, "There already exists a backup with the name: " + data.Backup.Name);
                            return;
                        }

                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);
                    }

                    bw.OutputOK(new { status = "OK", ID = data.Backup.ID });
                }
            }
            catch (Exception ex)
            {
                if (data == null)
                    ReportError(response, bw, string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                else
                    ReportError(response, bw, string.Format("Unable to save schedule or backup object: {0}", ex.Message));
            }
        }
    }
}

