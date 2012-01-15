#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
using System.Text;
using System.Xml.Serialization;

namespace Duplicati.Library.Main.StateVerification
{
    /// <summary>
    /// Represents a single invocation of Duplicati
    /// </summary>
    [XmlType(TypeName="collection")]
    [XmlRootAttribute(ElementName="duplicati-state-db")]
    [Serializable()]
    public class InvocationCollection
    {
        [XmlAttribute("begin")]
        public DateTime BeginTime;
        [XmlAttribute("end")]
        public DateTime EndTime;
        [XmlAttribute("operation")]
        public string Operation;
        
        [XmlElement("events")]
        [XmlElement("list", typeof(ListLogEntry))]
        [XmlElement("put", typeof(PutLogEntry))]
        [XmlElement("get", typeof(GetLogEntry))]
        [XmlElement("delete", typeof(DeleteLogEntry))]
        [XmlElement("createfolder", typeof(CreateFolderLogEntry))]
        public List<LogEntryBase> Entries;
    }
    
    /// <summary>
    /// The base for all operations performed on a remote folder
    /// </summary>
    [Serializable()]
    public abstract class LogEntryBase
    {
        [XmlAttribute("op")]
        public BackendOperation Operation;
        [XmlElement("message")]
        public string LogMessage;
        [XmlAttribute("success")]
        public bool Success;
        [XmlAttribute("timestamp")]
        public DateTime Timestamp = DateTime.Now;
    }
    
    /// <summary>
    /// A single remote file
    /// </summary>
    [XmlType(TypeName="file")]
    public class FileItem
    {
        [XmlAttribute("name")]
        public string Name;
        [XmlAttribute("size")]
        public long Size;
        [XmlAttribute("modified")]
        public DateTime Modified;
    }
    
    /// <summary>
    /// Representation of the LIST operation
    /// </summary>
    [XmlType(TypeName="list")]
    public class ListLogEntry : LogEntryBase
    {
        [XmlElement("contents")]
        public List<FileItem> Files;
    }
    
    /// <summary>
    /// Representation of the file based entries
    /// </summary>
    public abstract class FileEntryType : LogEntryBase
    {
        [XmlElement("file")]
        public FileItem File;
    }
    
    /// <summary>
    /// Representation of a DELETE operation
    /// </summary>
    [XmlType(TypeName="delete")]
    public class DeleteLogEntry : FileEntryType 
    {
    }

    /// <summary>
    /// Representation of the GET operation
    /// </summary>
    [XmlType(TypeName="get")]
    public class GetLogEntry : FileEntryType 
    {
    }

    /// <summary>
    /// Representation of the PUT operation
    /// </summary>
    [XmlType(TypeName="put")]
    public class PutLogEntry : FileEntryType 
    {
    }
    
    /// <summary>
    /// Representation of the CREATEFOLDER operation
    /// </summary>
    [XmlType(TypeName="createfolder")]
    public class CreateFolderLogEntry : LogEntryBase
    {
    }
}

