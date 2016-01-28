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

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Updates : IRESTMethodPOST
    {        public void POST(string key, RequestInfo info)        {            switch ((key ?? "").ToLowerInvariant())            {                case "check":                    Program.UpdatePoller.CheckNow();                    info.OutputOK();                    return;                case "install":                    Program.UpdatePoller.InstallUpdate();                    info.OutputOK();                    return;                case "activate":                    if (Program.WorkThread.CurrentTask != null || Program.WorkThread.CurrentTasks.Count != 0)                    {                        info.ReportServerError("Cannot activate update while task is running or scheduled");                    }                    else                    {                        Program.UpdatePoller.ActivateUpdate();                        info.OutputOK();                    }                    return;                                default:                    info.ReportClientError("No such action", System.Net.HttpStatusCode.NotFound);                    return;            }        }
    }
}

