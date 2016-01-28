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
using System;using System.Linq;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Notification : IRESTMethodGET, IRESTMethodDELETE
    {
        public void GET(string key, RequestInfo info)        {            long id;            if (!long.TryParse(key, out id))            {                info.ReportClientError("Invalid ID");                return;            }            var el = Program.DataConnection.GetNotifications().Where(x => x.ID == id).FirstOrDefault();            if (el == null)                info.ReportClientError("No such notification", System.Net.HttpStatusCode.NotFound);            else                info.OutputOK(el);        }        public void DELETE(string key, RequestInfo info)        {            long id;            if (!long.TryParse(key, out id))            {                info.ReportClientError("Invalid ID");                return;            }            var el = Program.DataConnection.GetNotifications().Where(x => x.ID == id).FirstOrDefault();            if (el == null)                info.ReportClientError("No such notification", System.Net.HttpStatusCode.NotFound);            else            {                Program.DataConnection.DismissNotification(id);                info.OutputOK();            }        }    }
}

