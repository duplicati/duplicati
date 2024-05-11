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
using System.Collections.Generic;
using Duplicati.Library.RestAPI;

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
                var task = FIXMEGlobal.WorkThread.CurrentTask;
                var tasks = FIXMEGlobal.WorkThread.CurrentTasks;

                if (task != null && task.TaskID == taskid)
                {
                    info.OutputOK(new { Status = "Running" });
                    return;
                }

                if (tasks.FirstOrDefault(x => x.TaskID == taskid) == null)
                {
                    KeyValuePair<long, Exception>[] matches;
                    lock(FIXMEGlobal.MainLock)
                        matches = FIXMEGlobal.TaskResultCache.Where(x => x.Key == taskid).ToArray();
                    
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
                var task = FIXMEGlobal.WorkThread.CurrentTask;
                var tasks = FIXMEGlobal.WorkThread.CurrentTasks;

                if (task != null)
                    tasks.Insert(0, task);

                task = tasks.FirstOrDefault(x => x.TaskID == taskid);
                if (task == null)
                {
                    info.ReportClientError("No such task", System.Net.HttpStatusCode.NotFound);
                    return;
                }

                switch (parts.Last().ToLowerInvariant())
                {
                    case "stopaftercurrentfile":
                        task.Stop(allowCurrentFileToFinish: true);
                        info.OutputOK();
                        return;

                    case "stopnow":
                        task.Stop(allowCurrentFileToFinish: false);
                        info.OutputOK();
                        return;

                    case "abort":
                        task.Abort();
                        info.OutputOK();
                        return;
                }
            }

            info.ReportClientError("Invalid or missing task id", System.Net.HttpStatusCode.NotFound);
        }
    }
}

