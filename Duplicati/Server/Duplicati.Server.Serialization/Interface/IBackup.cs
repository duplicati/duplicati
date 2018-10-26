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
using System.Collections.Generic;

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// All settings for a single backup
    /// </summary>
    public interface IBackup
    {
        /// <summary>
        /// The backup ID
        /// </summary>
        string ID { get; set; }
        /// <summary>
        /// The backup name
        /// </summary>
        string Name { get; set; }
        /// <summary>
        /// The backup description
        /// </summary>
        string Description { get; set; }
        /// <summary>
        /// The backup tags
        /// </summary>
        string[] Tags { get; set; }
        /// <summary>
        /// The backup target url, excluding username/password
        /// </summary>
        string TargetURL { get; set; }
        /// <summary>
        /// The path to the local database
        /// </summary>
        string DBPath { get; }
        
        /// <summary>
        /// The backup source folders and files
        /// </summary>
        string[] Sources { get; set; }
        
        /// <summary>
        /// The backup settings
        /// </summary>
        ISetting[] Settings { get; set; }
        
        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        IFilter[] Filters { get; set; }
        
        /// <summary>
        /// The backup metadata
        /// </summary>
        IDictionary<string, string> Metadata { get; set; }
        
        /// <summary>
        /// Gets a value indicating if this instance is not persisted to the database
        /// </summary>
        bool IsTemporary { get; }
    }
}

