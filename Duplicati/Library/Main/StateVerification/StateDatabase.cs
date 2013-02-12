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
    /// Possible backend operations
    /// </summary>
    public enum BackendOperation 
    {
        Get,
        Put,
        List,
        Delete,
        CreateFolder
    }
    
    /// <summary>
    /// Maintains local state information about the expected remote directory contents
    /// </summary>
    public class StateDatabase : Duplicati.Library.Interface.IBackendInteraction, IEnumerable<LogEntryBase>
    {
        private string m_filename;
        private List<InvocationCollection> m_invocations;
        private InvocationCollection m_current;
        private CommunicationStatistics m_stats;
        
        public StateDatabase(string filename, CommunicationStatistics stats)
        {
            m_filename = filename;
            m_stats = stats;
            
            if (System.IO.File.Exists(filename)) 
            {
                XmlSerializer s = new XmlSerializer(typeof(List<InvocationCollection>), new XmlRootAttribute("duplicati-state-db"));
                using (System.IO.FileStream fs = System.IO.File.OpenRead(m_filename))
                    m_invocations = (List<InvocationCollection>)s.Deserialize(fs);
            }
            else 
            {
                m_invocations = new List<InvocationCollection>();
            }
            
            m_current = new InvocationCollection();
            m_current.BeginTime = DateTime.Now;
            m_current.Entries = new List<LogEntryBase>();
        }
        
        public void BeginOperation(string name) 
        {
            m_current.Operation = name;
        }
        
        public void EndOperation() 
        {
            m_current.EndTime = DateTime.Now;
            Save();
        }
        
        
        private class ItemEnumerator : IEnumerator<LogEntryBase>
        {
            private List<InvocationCollection> m_invocations;
            private InvocationCollection m_current;
            private int m_i;
            private int m_j;
            private bool m_isCurrentAdded;
            
            public ItemEnumerator(List<InvocationCollection> invocations, InvocationCollection current) 
            {
                m_invocations = invocations;
                m_current = current;
                m_isCurrentAdded = invocations.Count > 0 && invocations[invocations.Count - 1] == current;
                this.Reset();
            }

            #region IEnumerator[LogEntryBase] implementation
            public LogEntryBase Current 
            {
                get 
                {
                    if (m_i >= m_invocations.Count + (m_isCurrentAdded ? 0 : 1))
                        return null;
                    
                    InvocationCollection ic = (m_i >= m_invocations.Count) ? m_current : m_invocations[m_i];
                    if (m_j >= ic.Entries.Count)
                        return null;
                    
                    return ic.Entries[m_j];
                }
            }
            #endregion

            #region IEnumerator implementation
            public bool MoveNext ()
            {
                if (m_i >= m_invocations.Count + (m_isCurrentAdded ? 0 : 1))
                    return false;
                
                InvocationCollection ic = (m_i >= m_invocations.Count) ? m_current : m_invocations[m_i];
                m_j++;

                if (m_j >= ic.Entries.Count)
                {
                    m_i++;
                    m_j = -1;
                    return MoveNext();
                }
                
                return true;
            }

            public void Reset ()
            {
                m_i = 0;
                m_j = -1;
            }

            object System.Collections.IEnumerator.Current 
            {
                get 
                {
                    return this.Current;
                }
            }
            #endregion

            #region IDisposable implementation
            public void Dispose ()
            {
            }
            #endregion
        }
        
        private int FindFileItem(List<FileItem> files, string name)
        {
            for(int i = 0; i < files.Count; i++)
                if (files[i].Name.Equals(name, StringComparison.InvariantCulture))
                    return i;
            
            return -1;
        }
        
        public List<FileItem> GetAndVerifyCurrentState()
        {
            //First we find the most recent LIST operation
            List<FileItem> last_list = new List<FileItem>();
            foreach(LogEntryBase leb in this)
            {
                if (leb.Success)
                {
                    if (leb.Operation == BackendOperation.List)
                    {
                        //If there was a list operation before, check if we have the same contents
                        List<FileItem> new_list = ((ListLogEntry)leb).Files;
                        if (last_list != null && last_list.Count != 0)
                        {
                            StringBuilder changeMessages = new StringBuilder();
                            
                            foreach(FileItem fi in new_list)
                            {
                                int ix = FindFileItem(last_list, fi.Name);
                                if (ix == -1)
                                    changeMessages.AppendLine("A file was added unexpectedly: " + fi.Name);
                                else
                                    last_list.RemoveAt(ix);
                            }
                            
                            foreach(FileItem fi in last_list)
                                changeMessages.AppendLine("A file was removed unexpectedly: " + fi.Name);
                            
                            if (changeMessages.Length > 0 && m_stats != null)
                            {
                                m_stats.LogWarning("Change found in list @" + leb.Timestamp
                                                   + Environment.NewLine +
                                                   changeMessages.ToString(), null);
                            }
                        }
                        
                        last_list = new List<FileItem>(new_list);
                    }

                    if (last_list != null) 
                    {
                        if (leb.Operation == BackendOperation.Put)
                        {
                            //Check for overwrite
                            int ix = FindFileItem(last_list, ((PutLogEntry)leb).File.Name);
                            if (ix != -1)
                                last_list.RemoveAt(ix);
                            last_list.Add(((PutLogEntry)leb).File);
                                
                        }
                        else if (leb.Operation == BackendOperation.Delete)
                        {
                            string name = ((DeleteLogEntry)leb).File.Name;
                            int ix = FindFileItem(last_list, name);
                            
                            if (ix == -1) 
                            {
                                if (m_stats != null)
                                    m_stats.LogWarning("Delete @" + leb.Timestamp + " for file not found: " + name, null);
                            }
                            else
                                last_list.RemoveAt(ix);
                        }
                    }
                }
            }

            return last_list;
        }
        
        public void Save() 
        {
            if (m_invocations.Count == 0 || m_invocations[m_invocations.Count -1] != m_current)
                m_invocations.Add(m_current);
            
            XmlSerializer s = new XmlSerializer(typeof(List<InvocationCollection>), new XmlRootAttribute("duplicati-state-db"));
            using (System.IO.FileStream fs = System.IO.File.OpenWrite(m_filename))
                s.Serialize(fs, m_invocations);
        }
                
        public void RegisterGet(Duplicati.Library.Interface.IFileEntry entry, bool success, String errorMessage)
        {
            m_current.Entries.Add(new GetLogEntry() {
                Operation = BackendOperation.Get,
                Success = success,
                LogMessage = errorMessage,
                File = new FileItem() {
                    Name = entry.Name,
                    Size = entry.Size,
                    Modified = entry.LastModification
                }
            });
        }

        public void RegisterDelete(Duplicati.Library.Interface.IFileEntry entry, bool success, String errorMessage)
        {
            m_current.Entries.Add(new DeleteLogEntry() {
                Operation = BackendOperation.Delete,
                Success = success,
                LogMessage = errorMessage,
                File = new FileItem() {
                    Name = entry.Name,
                    Size = entry.Size,
                    Modified = entry.LastModification
                }
            });
        }

        public void RegisterPut(Duplicati.Library.Interface.IFileEntry entry, bool success, String errorMessage)
        {
            m_current.Entries.Add(new PutLogEntry() {
                Operation = BackendOperation.Put,
                Success = success,
                LogMessage = errorMessage,
                File = new FileItem() {
                    Name = entry.Name,
                    Size = entry.Size,
                    Modified = entry.LastModification
                }
            });
        }
        
        public void RegisterCreateFolder(bool success, String errorMessage)
        {
            m_current.Entries.Add(new CreateFolderLogEntry() {
                Operation = BackendOperation.CreateFolder,
                Success = success,
                LogMessage = errorMessage
            });
        }
        
        public void RegisterList(List<Duplicati.Library.Interface.IFileEntry> files, bool success, String errorMessage) 
        {
            var ent = new ListLogEntry() {
                Operation = BackendOperation.List,
                LogMessage = errorMessage,
                Success = success,
                Files = files == null ? null : new List<FileItem>()
            };
            
            if (files != null)
                foreach(var f in files)
                    ent.Files.Add(new FileItem() {
                        Name = f.Name,
                        Size = f.Size,
                        Modified = f.LastModification
                    });
            
            m_current.Entries.Add(ent);
   
            //As we have added the entry, we can just run the normal verification process
            GetAndVerifyCurrentState();
        }

        #region IEnumerable[LogEntryBase] implementation
        public IEnumerator<LogEntryBase> GetEnumerator ()
        {
            return new ItemEnumerator(m_invocations, m_current);
        }
        #endregion

        #region IEnumerable implementation
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
        {
            return GetEnumerator();
        }
        #endregion
    }
}

