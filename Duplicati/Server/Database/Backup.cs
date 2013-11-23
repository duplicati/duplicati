//  Copyright (C) 2011, Kenneth Skovhede

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
using Duplicati.Server.Serialization.Interface;
using System.Collections.Generic;

namespace Duplicati.Server.Database
{
    public class Backup : IBackup
    {
    
        public Backup()
        {
            this.ID = -2;
        }
        
        internal void LoadChildren(Connection con)
        {
            if (this.ID < 0)
            {
                this.Sources = new string[0];
                this.Settings = new ISetting[0];
                this.Filters = new IFilter[0];
                this.Metadata = new Dictionary<string, string>();
            }
            else
            {
                this.Sources = con.GetSources(this.ID);
                this.Settings = con.GetSettings(this.ID);
                this.Filters = con.GetFilters(this.ID);
                this.Metadata = con.GetMetadata(this.ID);
            }
        }
    
        /// <summary>
        /// The backup ID
        /// </summary>
        public long ID { get; set; }
        /// <summary>
        /// The backup name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The backup tags
        /// </summary>
        public string[] Tags { get; set; }
        /// <summary>
        /// The backup target url, excluding username/password
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
    }
}

