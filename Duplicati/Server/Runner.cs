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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    public static class Runner
    {
        private class MessageSink : Duplicati.Library.Main.IMessageSink
        {
            private class ProgressState : Server.Serialization.Interface.IProgressEventData
            {
                private readonly long m_backupID;
                public long LastEventID { get; set; }
                
                internal Duplicati.Library.Main.BackendActionType m_backendAction;
                internal string m_backendPath;
                internal long m_backendFileSize;
                internal long m_backendFileProgress;
                internal long m_backendSpeed;
                
                internal string m_currentFilename;
                internal long m_currentFilesize;
                internal long m_currentFileoffset;
                
                internal Duplicati.Library.Main.OperationPhase m_phase;
                internal float m_overallProgress;
                internal long m_processedFileCount;
                internal long m_processedFileSize;
                internal long m_totalFileCount;
                internal long m_totalFileSize;
                internal bool m_stillCounting;
                
                public ProgressState(long backupId)
                {
                    m_backupID = backupId;
                }
                
                internal ProgressState Clone()
                {
                    return (ProgressState)this.MemberwiseClone();
                }

                #region IProgressEventData implementation
                public long BackupID { get { return m_backupID; } }
                public string BackendAction { get { return m_backendAction.ToString(); } }
                public string BackendPath { get { return m_backendPath; } }
                public long BackendFileSize { get { return m_backendFileSize; } }
                public long BackendFileProgress { get { return m_backendFileProgress; } }
                public long BackendSpeed { get { return m_backendSpeed; } }
                public string CurrentFilename { get { return m_currentFilename; } }
                public long CurrentFilesize { get { return m_currentFilesize; } }
                public long CurrentFileoffset { get { return m_currentFileoffset; } }
                public string Phase { get { return  m_phase.ToString(); } }
                public float OverallProgress { get { return m_overallProgress; } }
                public long ProcessedFileCount { get { return m_processedFileCount; } }
                public long ProcessedFileSize { get { return m_processedFileSize; } }
                public long TotalFileCount { get { return m_totalFileCount; } }
                public long TotalFileSize { get { return m_totalFileSize; } }
                public bool StillCounting { get { return m_stillCounting; } }
                #endregion
            }
                        
            private ProgressState m_state;
            private Duplicati.Library.Main.IBackendProgress m_backendProgress;
            private Duplicati.Library.Main.IOperationProgress m_operationProgress;
            private object m_lock = new object();
            
            public MessageSink(long backupId)
            {
                m_state = new ProgressState(backupId);
            }
            
            public Server.Serialization.Interface.IProgressEventData Copy()
            {
                lock(m_lock)
                {
                    if (m_backendProgress != null)
                        m_backendProgress.Update(out m_state.m_backendAction, out m_state.m_backendPath, out m_state.m_backendFileSize, out m_state.m_backendFileProgress, out m_state.m_backendSpeed);
                    if (m_operationProgress != null)
                    {
                        m_operationProgress.UpdateFile(out m_state.m_currentFilename, out m_state.m_currentFilesize, out m_state.m_currentFileoffset);
                        m_operationProgress.UpdateOverall(out m_state.m_phase, out m_state.m_overallProgress, out m_state.m_processedFileCount, out m_state.m_processedFileSize, out m_state.m_totalFileCount, out m_state.m_totalFileSize, out m_state.m_stillCounting);
                    }
                        
                    return m_state.Clone();
                }
            }
            
            #region IMessageSink implementation
            public void BackendEvent(Duplicati.Library.Main.BackendActionType action, Duplicati.Library.Main.BackendEventType type, string path, long size)
            {
                lock(m_lock)
                {
                    m_state.m_backendAction = action;
                    m_state.m_backendPath = path;
                    if (type == Duplicati.Library.Main.BackendEventType.Started)
                        m_state.m_backendFileSize = size;
                    else if (type == Duplicati.Library.Main.BackendEventType.Progress)
                        m_state.m_backendFileProgress = size;
                    else
                    {
                        m_state.m_backendFileSize = 0;
                        m_state.m_backendFileProgress = 0;
                        m_state.m_backendSpeed = 0;
                    }
                }
            }
            public void VerboseEvent(string message, object[] args)
            {
            }
            public void MessageEvent(string message)
            {
            }
            public void RetryEvent(string message, Exception ex)
            {
            }
            public void WarningEvent(string message, Exception ex)
            {
            }
            public void ErrorEvent(string message, Exception ex)
            {
            }
            public void DryrunEvent(string message)
            {
            }
            public Duplicati.Library.Main.IBackendProgress BackendProgress
            {
                set
                {
                    lock(m_lock)
                        m_backendProgress = value;
                }
            }
            public Duplicati.Library.Main.IOperationProgress OperationProgress
            {
                set
                {                    
                    lock(m_lock)
                        m_operationProgress = value;
                }
            }
            #endregion
        }
        
        private static string DecodeSource(string n, long backupId)
        {
            if (string.IsNullOrWhiteSpace(n))
                return null;
            
            var t = n;
            if (n.StartsWith("%") && n.EndsWith("%"))
            {
                t = SpecialFolders.TranslateToPath(n);
                if (t == null)
                    t = System.Environment.ExpandEnvironmentVariables(n);
            }
            
            if (string.IsNullOrWhiteSpace(t) || (t.StartsWith("%") && t.EndsWith("%")))
            {
                Program.DataConnection.LogError(backupId, string.Format("Skipping source \"{0}\"", n), null);
                return null;
            }
            
            return t;
        }
    
        public static void Run(Tuple<long, Server.Serialization.DuplicatiOperation> item)
        {
            Duplicati.Server.Serialization.Interface.IBackup backup = null;
            
            
            try
            {
                backup = Program.DataConnection.GetBackup(item.Item1);
                if (backup == null)
                    throw new Exception(string.Format("No backup with ID: {0}", item.Item1));
                
                var options = ApplyOptions(backup, item.Item2, GetCommonOptions(backup, item.Item2));
                var sink = new MessageSink(backup.ID);
                Program.GenerateProgressState = () => sink.Copy();
                Program.StatusEventNotifyer.SignalNewEvent();            
                
                using(var controller = new Duplicati.Library.Main.Controller(backup.TargetURL, options, sink))
                {
                    switch (item.Item2)
                    {
                        case DuplicatiOperation.Backup:
                            {
                                var filter = ApplyFilter(backup, item.Item2, GetCommonFilter(backup, item.Item2));
                                var sources = 
                                        (from n in backup.Sources
                                        let p = DecodeSource(n, backup.ID)
                                        where !string.IsNullOrWhiteSpace(p)
                                        select p).ToArray();
                                
                                var r = controller.Backup(sources, filter);
                                UpdateMetadata(backup, r);
                            }
                            break;
                            
                        case DuplicatiOperation.List:
                            {
                                //TODO: Need to pass arguments
                                var r = controller.List();
                                UpdateMetadata(backup, r);
                            }
                            break;
                        case DuplicatiOperation.Repair:
                            {
                                var r = controller.Repair();
                                UpdateMetadata(backup, r);
                            }
                            break;
                        case DuplicatiOperation.Remove:
                            {
                                var r = controller.Delete();
                                UpdateMetadata(backup, r);
                            }
                            break;
                        case DuplicatiOperation.Restore:
                            {
                                //TODO: Need to pass arguments
                                var r = controller.Restore(new string[] { "*" });
                                UpdateMetadata(backup, r);
                            }
                            break;
                        case DuplicatiOperation.Verify:
                            {
                                //TODO: Need to pass arguments
                                var r = controller.Test();
                                UpdateMetadata(backup, r);
                            }
                            break;
                        default:
                            //TODO: Log this
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError(item.Item1, string.Format("Failed while executing \"{0}\" with id: {1}", item.Item2, item.Item1), ex);
                //TODO: Update metadata with the error here
            }
        }
        
        private static void UpdateMetadata(Duplicati.Server.Serialization.Interface.IBackup backup, object o)
        {
            if (o is Duplicati.Library.Interface.IBasicResults)
            {
                var r = (Duplicati.Library.Interface.IBasicResults)o;
                backup.Metadata["LastDuration"] = r.Duration.ToString();
            }
            
            if (o is Duplicati.Library.Interface.IParsedBackendStatistics)
            {
                var r = (Duplicati.Library.Interface.IParsedBackendStatistics)o;
                backup.Metadata["LastBackupDate"] = r.LastBackupDate.ToUniversalTime().ToString();
                backup.Metadata["BackupListCount"] = r.BackupListCount.ToString();
                backup.Metadata["TotalQuotaSpace"] = r.TotalQuotaSpace.ToString();
                backup.Metadata["FreeQuotaSpace"] = r.FreeQuotaSpace.ToString();
                backup.Metadata["AssignedQuotaSpace"] = r.AssignedQuotaSpace.ToString();
                
                backup.Metadata["TargetFilesSize"] = r.KnownFileSize.ToString();
                backup.Metadata["TargetFilesCount"] = r.KnownFileCount.ToString();
                backup.Metadata["TargetSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.KnownFileSize);
            }
            
            if (o is Duplicati.Library.Interface.IBackupResults)
            {
                var r = (Duplicati.Library.Interface.IBackupResults)o;
                backup.Metadata["SourceFilesSize"] = r.SizeOfExaminedFiles.ToString();
                backup.Metadata["SourceFilesCount"] = r.ExaminedFiles.ToString();
                backup.Metadata["SourceSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.SizeOfExaminedFiles);
            }
            
            Program.DataConnection.SetMetadata(backup.Metadata, backup.ID, null);
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }
        
        private static bool TestIfOptionApplies(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, string filter)
        {
            //TODO: Implement to avoid warnings
            return true;
        }
        
        private static Dictionary<string, string> ApplyOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Dictionary<string, string> options)
        {
            options["backup-name"] = backup.Name;
            options["dbpath"] = backup.DBPath;
            
            foreach(var o in backup.Settings)
                if (TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name] = o.Value;
                    
            return options;
        }
        
        private static Duplicati.Library.Utility.FilterExpression ApplyFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Duplicati.Library.Utility.FilterExpression filter)
        {
            var f2 = backup.Filters;
            if (f2 != null && f2.Length > 0)
                filter = new Duplicati.Library.Utility.FilterExpression[] { filter }.Union(
                    (from n in f2
                    orderby n.Order
                    select new Duplicati.Library.Utility.FilterExpression(n.Expression, n.Include))
                )
                .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));
            
            return filter;
        }
        
        private static Dictionary<string, string> GetCommonOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            return 
                (from n in Program.DataConnection.Settings
                where TestIfOptionApplies(backup, mode, n.Filter)
                select n).ToDictionary(k => k.Name, k => k.Value);
        }
        
        private static Duplicati.Library.Utility.FilterExpression GetCommonFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            var filters = Program.DataConnection.Filters;
            if (filters == null || filters.Length == 0)
                return null;
            
           return   
                (from n in filters
                orderby n.Order
                select new Duplicati.Library.Utility.FilterExpression(n.Expression, n.Include))
                .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));
        }
    }
}

