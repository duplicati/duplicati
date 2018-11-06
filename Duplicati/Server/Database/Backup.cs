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
using Duplicati.Server.Serialization.Interface;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Duplicati.Server.Database
{
    public class Backup : IBackup
    {
    
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
        public bool IsTemporary { get { return ID == null ? false : ID.IndexOf("-", StringComparison.Ordinal) > 0; } }

        /// <summary>
        /// Sanitizes the backup TargetUrl
        /// </summary>
        public void SanitizeTargetUrl()
        {
            var url = this.TargetURL;
            // Remove authid
            url = Regex.Replace(url, "authid=[^&\n]+[&]?", "");
            // remove auth-password
            url = Regex.Replace(url, "auth-password=[^&\n]+[&]?", "");
            // remove backupsia-password
            url = Regex.Replace(url, "sia-password=[^&\n]+[&]?", "");
            // Remove edge case of '?&'
            url = Regex.Replace(url, Regex.Escape("?&"), "");
            // Remove edge case of '&' at end of line
            url = Regex.Replace(url, "&$", "");
            // Remove edge case of '?' at end of line
            url = Regex.Replace(url, Regex.Escape("?") + "$", "");
            this.TargetURL = url;
        }
    }
}

