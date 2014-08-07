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

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private void DeleteBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var backup = Program.DataConnection.GetBackup(input["id"].Value);
            if (backup == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
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
                        if (!bool.TryParse(input["force"].Value, out force))
                            force = false;

                        if (!force)
                        {
                            bw.WriteJsonObject(new { status = "failed", reason = "backup-in-progress" });
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
                                bw.WriteJsonObject(new { status = "failed", reason = "backup-unstoppable" });
                                return;
                            }
                        }

                        if (hasPaused)
                            Program.LiveControl.Resume();
                    }
                }
                catch (Exception ex)
                {
                    bw.WriteJsonObject(new { status = "error", message = ex.Message });
                    return;
                }
            }


            //var dbpath = backup.DBPath;
            Program.DataConnection.DeleteBackup(backup);

            // TODO: Before we activate this, 
            // we need some strategy to figure out
            // if the db is shared with something else
            // like the commandline or another backup
            /*try
            {
                if (System.IO.File.Exists(dbpath))
                    System.IO.File.Delete(dbpath);
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError(null, string.Format("Failed to delete database: {0}", dbpath), ex);
            }*/

            //We have fiddled with the schedules
            Program.Scheduler.Reschedule();

            bw.OutputOK();
        }
    }
}

