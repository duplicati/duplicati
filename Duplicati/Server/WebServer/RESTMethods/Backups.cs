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
using System;using System.Linq;using System.Collections.Generic;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Backups : IRESTMethodGET, IRESTMethodDocumented
    {        private class AddOrUpdateBackupData        {            public Database.Schedule Schedule { get; set;}            public Database.Backup Backup { get; set;}        }        public void GET(string key, RequestInfo info)        {            var schedules = Program.DataConnection.Schedules;            var backups = Program.DataConnection.Backups;            var all = from n in backups                select new AddOrUpdateBackupData() {                Backup = (Database.Backup)n,                Schedule =                     (from x in schedules                        where x.Tags != null && x.Tags.Contains("ID=" + n.ID)                        select (Database.Schedule)x).FirstOrDefault()                };            info.BodyWriter.OutputOK(all.ToArray());        }
        public string Description { get { return "Return a list of current backups and their schedules"; } }        public IEnumerable<KeyValuePair<string, Type>> Types        {            get            {                return new KeyValuePair<string, Type>[] {                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(AddOrUpdateBackupData[]))                };            }        }    }
}

