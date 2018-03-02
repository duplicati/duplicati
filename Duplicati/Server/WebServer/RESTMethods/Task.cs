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

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Task : IRESTMethodGET, IRESTMethodPOST
    {
        public void GET(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' }, 2);
            long taskid;
            if (long.TryParse(parts.FirstOrDefault(), out taskid))
            {
                var task = Program.WorkThread.CurrentTask;
                var tasks = Program.WorkThread.CurrentTasks;

                if (task != null && task.TaskID == taskid)
                {
                    info.OutputOK(new { Status = "Running" });
                    return;
                }

                task = tasks.Where(x => x.TaskID == taskid).FirstOrDefault();
                if (tasks.Where(x => x.TaskID == taskid).FirstOrDefault() == null)
                {
                    KeyValuePair<long, Exception>[] matches;
                    lock(Program.MainLock)
                        matches = Program.TaskResultCache.Where(x => x.Key == taskid).ToArray();
                    
                    if (matches.Length == 0)
                        info.ReportClientError("No such task found", System.Net.HttpStatusCode.NotFound);
                    else
                        info.OutputOK(new { 
                            Status = matches[0].Value == null ? "Completed" : "Failed", 
                            ErrorMessage = matches[0].Value == null ? null : matches[0].Value.Message,
                            Exception = matches[0].Value == null ? null : matches[0].Value.ToString() 
                        });                            
                }
                else
                {
                    info.OutputOK(new { Status = "Waiting" });
                }
            }
            else
            {
                info.ReportClientError("Invalid request", System.Net.HttpStatusCode.BadRequest);
            }
        }

        public void POST(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' }, 2);
            long taskid;
            if (parts.Length == 2 && long.TryParse(parts.First(), out taskid))
            {
                var task = Program.WorkThread.CurrentTask;
                var tasks = Program.WorkThread.CurrentTasks;

                if (task != null)
                    tasks.Insert(0, task);

                task = tasks.Where(x => x.TaskID == taskid).FirstOrDefault();
                if (task == null)
                {
                    info.ReportClientError("No such task", System.Net.HttpStatusCode.NotFound);
                    return;
                }

                switch (parts.Last().ToLowerInvariant())
                {
                    case "abort":
                        task.Abort();
                        info.OutputOK();
                        return;

                    case "stop":
                        task.Stop();
                        info.OutputOK();
                        return;
                }
            }

            info.ReportClientError("Invalid or missing task id", System.Net.HttpStatusCode.NotFound);
        }
    }
}

