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
        public interface IRunnerData : Duplicati.Server.Serialization.Interface.IQueuedTask
        {
            Duplicati.Server.Serialization.DuplicatiOperation Operation { get; }
            Duplicati.Server.Serialization.Interface.IBackup Backup { get; }
            IDictionary<string, string> ExtraOptions { get; }
            string[] FilterStrings { get; }
        }
        
        private class RunnerData : IRunnerData
        {
            private static long RunnerTaskID = 1;
            
            public Duplicati.Server.Serialization.DuplicatiOperation Operation { get; internal set; }
            public Duplicati.Server.Serialization.Interface.IBackup Backup { get; internal set; }
            public IDictionary<string, string> ExtraOptions { get; internal set; }
            public string[] FilterStrings { get; internal set; }
            
            public string BackupID { get { return Backup.ID; } }
            public long TaskID { get { return m_taskID; } }
            
            private readonly long m_taskID;
            
            public RunnerData()
            {
                m_taskID = System.Threading.Interlocked.Increment(ref RunnerTaskID);
            }
        }
        
        public static IRunnerData CreateTask(Duplicati.Server.Serialization.DuplicatiOperation operation, Duplicati.Server.Serialization.Interface.IBackup backup, IDictionary<string, string> extraOptions = null, string[] filterStrings = null)
        {
            return new RunnerData() {
                Operation = operation,
                Backup = backup,
                ExtraOptions = extraOptions,
                FilterStrings = filterStrings
            };
        }
        
        public static IRunnerData CreateListTask(Duplicati.Server.Serialization.Interface.IBackup backup, string[] filters, bool onlyPrefix, bool allVersions, bool folderContents, DateTime time)
        {
            var dict = new Dictionary<string, string>();
            if (onlyPrefix)
                dict["list-prefix-only"] = "true";
            if (allVersions)
                dict["all-versions"] = "true";
            if (time.Ticks > 0)
                dict["time"] = Duplicati.Library.Utility.Utility.SerializeDateTime(time.ToUniversalTime());
            if (folderContents)
                dict["list-folder-contents"] = "true";
            
            return CreateTask(
                DuplicatiOperation.List,
                backup,
                dict,
                filters);
        }

        public static IRunnerData CreateRestoreTask(Duplicati.Server.Serialization.Interface.IBackup backup, string[] filters, DateTime time, string restoreTarget, bool overwrite)
        {
            var dict = new Dictionary<string, string>();
            dict["time"] = Duplicati.Library.Utility.Utility.SerializeDateTime(time.ToUniversalTime());
            if (!string.IsNullOrWhiteSpace(restoreTarget))
                dict["restore-path"] = restoreTarget;
            if (overwrite)
                dict["overwrite"] = "true";
            
            return CreateTask(
                DuplicatiOperation.Restore,
                backup,
                dict,
                filters);            
        }        
        private class MessageSink : Duplicati.Library.Main.IMessageSink
        {
            private class ProgressState : Server.Serialization.Interface.IProgressEventData
            {
                private readonly string m_backupID;
                private readonly long m_taskID;
                
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
                
                public ProgressState(long taskId, string backupId)
                {
                    m_backupID = backupId;
                    m_taskID = taskId;
                }
                
                internal ProgressState Clone()
                {
                    return (ProgressState)this.MemberwiseClone();
                }

                #region IProgressEventData implementation
                public string BackupID { get { return m_backupID; } }
                public long TaskID { get { return m_taskID; } }
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
            
            public MessageSink(long taskId, string backupId)
            {
                m_state = new ProgressState(taskId, backupId);
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
        
        public static Duplicati.Library.Interface.IBasicResults Run(IRunnerData data, bool throwEx = false)
        {
            Duplicati.Server.Serialization.Interface.IBackup backup = data.Backup;
            
            try
            {                
                var options = ApplyOptions(backup, data.Operation, GetCommonOptions(backup, data.Operation));
                var sink = new MessageSink(data.TaskID, backup.ID);
                Program.GenerateProgressState = () => sink.Copy();
                Program.StatusEventNotifyer.SignalNewEvent();            
                
                if (data.ExtraOptions != null)
                    foreach(var k in data.ExtraOptions)
                        options[k.Key] = k.Value;
                
                using(var controller = new Duplicati.Library.Main.Controller(backup.TargetURL, options, sink))
                {
                    switch (data.Operation)
                    {
                        case DuplicatiOperation.Backup:
                            {
                                var filter = ApplyFilter(backup, data.Operation, GetCommonFilter(backup, data.Operation));
                                var sources = 
                                        (from n in backup.Sources
                                        let p = SpecialFolders.ExpandEnvironmentVariables(n)
                                        where !string.IsNullOrWhiteSpace(p)
                                        select p).ToArray();
                                
                                var r = controller.Backup(sources, filter);
                                UpdateMetadata(backup, r);
                                return r;
                            }                            
                        case DuplicatiOperation.List:
                            {
                                var r = controller.List(data.FilterStrings);
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Repair:
                            {
                                var r = controller.Repair();
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Remove:
                            {
                                var r = controller.Delete();
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Restore:
                            {
                                var r = controller.Restore(data.FilterStrings);
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Verify:
                            {
                                var r = controller.Test();
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        default:
                            //TODO: Log this
                            return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError(data.Backup.ID, string.Format("Failed while executing \"{0}\" with id: {1}", data.Operation, data.Backup.ID), ex);
                //TODO: Update metadata with the error here
                
                if (throwEx)
                    throw;
                
                return null;
            }
        }
        
        private static void UpdateMetadata(Duplicati.Server.Serialization.Interface.IBackup backup, Duplicati.Library.Interface.IParsedBackendStatistics r)
        {
            if (r != null)
            {
                backup.Metadata["LastBackupDate"] = r.LastBackupDate.ToUniversalTime().ToString();
                backup.Metadata["BackupListCount"] = r.BackupListCount.ToString();
                backup.Metadata["TotalQuotaSpace"] = r.TotalQuotaSpace.ToString();
                backup.Metadata["FreeQuotaSpace"] = r.FreeQuotaSpace.ToString();
                backup.Metadata["AssignedQuotaSpace"] = r.AssignedQuotaSpace.ToString();
                
                backup.Metadata["TargetFilesSize"] = r.KnownFileSize.ToString();
                backup.Metadata["TargetFilesCount"] = r.KnownFileCount.ToString();
                backup.Metadata["TargetSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.KnownFileSize);
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
                UpdateMetadata(backup, r);
            }
            
            if (o is Duplicati.Library.Interface.IBackupResults)
            {
                var r = (Duplicati.Library.Interface.IBackupResults)o;
                backup.Metadata["SourceFilesSize"] = r.SizeOfExaminedFiles.ToString();
                backup.Metadata["SourceFilesCount"] = r.ExaminedFiles.ToString();
                backup.Metadata["SourceSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.SizeOfExaminedFiles);
                if (r.BackendStatistics is Duplicati.Library.Interface.IParsedBackendStatistics)
                    UpdateMetadata(backup, (Duplicati.Library.Interface.IParsedBackendStatistics)r.BackendStatistics);
            }
            
            if (!backup.IsTemporary)
                Program.DataConnection.SetMetadata(backup.Metadata, long.Parse(backup.ID), null);
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }
        
        private static bool TestIfOptionApplies(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, string filter)
        {
            //TODO: Implement to avoid warnings
            return true;
        }
        
        private static void DisableModule(string module, Dictionary<string, string> options)
        {
            string disabledModules;
            string enabledModules;
            
            if (options.TryGetValue("enable-module", out enabledModules))
            {
                var emods = (enabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                options["enable-module"] = string.Join(",", emods.Where(x => module.Equals(x, StringComparison.InvariantCultureIgnoreCase)));
            }
            
            options.TryGetValue("disable-module", out disabledModules);
            var mods = (disabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            options["disable-module"] = string.Join(",", mods.Union(new string[] { module }).Distinct(StringComparer.InvariantCultureIgnoreCase));
        }
        
        private static Dictionary<string, string> ApplyOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Dictionary<string, string> options)
        {
            options["backup-name"] = backup.Name;
            options["dbpath"] = backup.DBPath;
            
            // Apply normal options
            foreach(var o in backup.Settings)
                if (!o.Name.StartsWith("--") && TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name] = o.Value;

            // Apply override options
            foreach(var o in backup.Settings)
                if (o.Name.StartsWith("--") && TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name.Substring(2)] = o.Value;
            
            
            // The server hangs if the module is enabled as there is no console attached
            DisableModule("console-password-input", options);
            
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

