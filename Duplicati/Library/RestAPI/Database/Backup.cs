// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Server.Serialization.Interface;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Duplicati.Server.Database
{
    public class Backup : IBackup
    {
        // Sensitive information that may be stored in TargetUrl
        private readonly string[] UrlPasswords = {
            "authid",
            "auth-password",
            "sia-password",
            "storj-secret",
            "storj-shared-access",
        };

        // Sensitive information that may be stored in Settings
        private readonly string[] SettingPasswords = {
            "passphrase",
            "--authid",
            "--send-mail-password",
            "--send-xmpp-password",
        };

        public Backup()
        {
            this.ID = null;
        }

        internal void LoadChildren(Connection con)
        {
            if (this.IsTemporary)
            {
                this.Sources = new string[0];
                this.Settings = new ISetting[0];
                this.Filters = new IFilter[0];
                this.Metadata = new Dictionary<string, string>();
            }
            else
            {
                var id = long.Parse(this.ID);
                this.Sources = con.GetSources(id);
                this.Settings = con.GetSettings(id);
                this.Filters = con.GetFilters(id);
                this.Metadata = con.GetMetadata(id);
            }
        }

        protected void SetDBPath(string path) => this.DBPath = path;

        /// <summary>
        /// The backup ID
        /// </summary>
        public string ID { get; set; }
        /// <summary>
        /// The backup name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The backup description
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// The backup tags
        /// </summary>
        public string[] Tags { get; set; }
        /// <summary>
        /// The backup target url
        /// </summary>
        public string TargetURL { get; set; }
        /// <summary>
        /// The path to the local database
        /// </summary>
        public string DBPath { get; internal set; }

        /// <summary>
        /// The backup source folders and files
        /// </summary>
        public string[] Sources { get; set; }

        /// <summary>
        /// The backup settings
        /// </summary>
        public ISetting[] Settings { get; set; }

        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        public IFilter[] Filters { get; set; }

        /// <summary>
        /// The backup metadata
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Gets a value indicating if this instance is not persisted to the database
        /// </summary>        
        public bool IsTemporary { get { return ID != null && ID.IndexOf("-", StringComparison.Ordinal) > 0; } }

        /// <summary>
        /// Sanitizes the backup TargetUrl from any fields in the PasswordFields list.
        /// </summary>
        public void SanitizeTargetUrl()
        {
            var url = new Duplicati.Library.Utility.Uri(this.TargetURL);
            NameValueCollection filteredParameters = new NameValueCollection();
            if (url.Query != null)
            {
                // We cannot use url.QueryParameters since it contains decoded parameter values, which
                // breaks assumptions made by the decode_uri function in AppUtils.js. Since we are simply
                // removing password parameters, we will leave the parameters as they are in the target URL.
                filteredParameters = Library.Utility.Uri.ParseQueryString(url.Query, false);
                foreach (string field in this.UrlPasswords)
                {
                    filteredParameters.Remove(field);
                }
            }
            url = url.SetQuery(Duplicati.Library.Utility.Uri.BuildUriQuery(filteredParameters));
            this.TargetURL = url.ToString();
        }

        /// <summary>
        /// Sanitizes the settings from any fields in the PasswordFields list.
        /// </summary>
        public void SanitizeSettings()
        {
            this.Settings = this.Settings.Where((setting) => !SettingPasswords.Contains(setting.Name)).ToArray();
        }
    }
}

