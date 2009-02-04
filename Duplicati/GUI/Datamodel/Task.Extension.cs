#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;


namespace Duplicati.Datamodel
{
    public partial class Task
    {
        private SettingsHelper<TaskSetting, string, string> m_settings;

        public IDictionary<string, string> Settings
        {
            get
            {
                if (m_settings == null)
                    m_settings = new SettingsHelper<TaskSetting, string, string>(this.DataParent, this.TaskSettings, "Name", "Value");

                return m_settings;
            }
        }

        public Backends.IBackend Backend
        {
            get
            {
                //TODO: This should be more dynamic
                switch (this.Service.Trim().ToLower())
                {
                    case "ssh":
                        return new Backends.SSH(this);
                    case "s3":
                        return new Backends.S3(this);
                    case "file":
                        return new Backends.File(this);
                    case "ftp":
                        return new Backends.FTP(this);
                }

                return null;
            }
        }

        public string GetDestinationPath()
        {
            return this.Backend.GetDestinationPath();
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            this.Backend.GetOptions(options);

            if (this.Filters.Count > 0)
                options["filter"] = this.EncodedFilter; ;
        }

        public string EncodedFilter
        {
            get
            {
                if (this.Filters.Count <= 0)
                    return "";

                List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
                foreach (TaskFilter f in this.SortedFilters)
                    filters.Add(new KeyValuePair<bool, string>(f.Include, f.Filter));

                return Library.Core.FilenameFilter.EncodeAsFilter(filters);
            }
            set
            {
                List<TaskFilter> filters = new List<TaskFilter>();
                foreach(KeyValuePair<bool, string> f in Library.Core.FilenameFilter.DecodeFilter(value))
                {
                    TaskFilter tf = this.DataParent.Add<TaskFilter>();
                    tf.Filter = f.Value;
                    tf.Include = f.Key;
                }

                this.SortedFilters = filters.ToArray();
            }
        }

        public bool ExistsInDb
        {
            get { return this.ID > 0; }
        }


        public TaskFilter[] SortedFilters
        {
            get
            {
                return System.Data.LightDatamodel.Query.Parse("ORDER BY SortOrder").EvaluateList<TaskFilter>(this.Filters).ToArray();
            }
            set
            {
                foreach (TaskFilter t in this.SortedFilters)
                    if (Array.IndexOf<TaskFilter>(value, t) >= 0)
                        this.Filters.Remove(t);
                    else
                        t.DataParent.DeleteObject(t);

                int index = 0;
                foreach (TaskFilter t in value)
                {
                    t.SortOrder = index++;
                    this.Filters.Add(t);
                }
            }
        }

        }
}
