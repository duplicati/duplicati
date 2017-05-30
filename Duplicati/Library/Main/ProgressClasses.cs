//  Copyright (C) 2013, Duplicati Team

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
using System.Linq;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Interface for recieving messages from the Duplicati operations
    /// </summary>
    public interface IMessageSink
    {
        void BackendEvent(BackendActionType action, BackendEventType type, string path, long size);
        void VerboseEvent(string message, object[] args);
        void MessageEvent(string message);
        void RetryEvent(string message, Exception ex);
        void WarningEvent(string message, Exception ex);
        void ErrorEvent(string message, Exception ex);
        void DryrunEvent(string message);
        
        /// <summary>
        /// Sets the backend progress update object
        /// </summary>
        /// <value>The backend progress update object</value>
        IBackendProgress BackendProgress { set; }
        
        /// <summary>
        /// Sets the operation progress update object
        /// </summary>
        /// <value>The operation progress update object</value>
        IOperationProgress OperationProgress { set; }
    }

    /// <summary>
    /// Helper class to allow setting multiple message sinks on a single controller
    /// </summary>
    public class MultiMessageSink : IMessageSink
    {
        /// <summary>
        /// The sinks in this instance
        /// </summary>
        private IMessageSink[] m_sinks;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.MultiMessageSink"/> class.
        /// </summary>
        /// <param name="sinks">The sinks to use.</param>
        public MultiMessageSink(params IMessageSink[] sinks)
        {
            m_sinks = (sinks ?? new IMessageSink[0]).Where(x => x != null).ToArray();
        }

        /// <summary>
        /// Appends a new sink to the list
        /// </summary>
        /// <param name="sink">The sink to append.</param>
        public void Append(IMessageSink sink)
        {
            if (sink == null)
                return;
            
            var na = new IMessageSink[m_sinks.Length + 1];
            Array.Copy(m_sinks, na, m_sinks.Length);
            na[na.Length - 1] = sink;
            m_sinks = na;
        }

        public IBackendProgress BackendProgress
        {
            set
            {
                foreach (var s in m_sinks)
                    s.BackendProgress = value;
            }
        }

        public IOperationProgress OperationProgress
        {
            set
            {
                foreach (var s in m_sinks)
                    s.OperationProgress = value;
            }
        }

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            foreach (var s in m_sinks)
                s.BackendEvent(action, type, path, size);
        }

        public void DryrunEvent(string message)
        {
            foreach (var s in m_sinks)
                s.DryrunEvent(message);
        }

        public void ErrorEvent(string message, Exception ex)
        {
            foreach (var s in m_sinks)
                s.ErrorEvent(message, ex);
        }

        public void MessageEvent(string message)
        {
            foreach (var s in m_sinks)
                s.MessageEvent(message);
        }

        public void RetryEvent(string message, Exception ex)
        {
            foreach (var s in m_sinks)
                s.RetryEvent(message, ex);
        }

        public void VerboseEvent(string message, object[] args)
        {
            foreach (var s in m_sinks)
                s.VerboseEvent(message, args);
        }

        public void WarningEvent(string message, Exception ex)
        {
            foreach (var s in m_sinks)
                s.WarningEvent(message, ex);
        }
    }
    
    /// <summary>
    /// Backend progress update object.
    /// The engine updates these statistics very often,
    /// so an event based system would take up too many resources.
    /// Instead, this interface allows the client to poll
    /// for updates as often as desired.
    /// </summary>
    public interface IBackendProgress
    {
        /// <summary>
        /// Update with the current action, path, size, progress and bytes_pr_second.
        /// </summary>
        /// <param name="action">The current action</param>
        /// <param name="path">The current path</param>
        /// <param name="size">The current size</param>
        /// <param name="progress">The current number of transferred bytes</param>
        /// <param name="bytes_pr_second">Transfer speed in bytes pr second, -1 for unknown</param>
        void Update(out BackendActionType action, out string path, out long size, out long progress, out long bytes_pr_second);
    }
    
    /// <summary>
    /// Interface for updating the backend progress
    /// </summary>
    internal interface IBackendProgressUpdater
    {
        /// <summary>
        /// Register the start of a new action
        /// </summary>
        /// <param name="action">The action that is starting</param>
        /// <param name="path">The path being operated on</param>
        /// <param name="size">The size of the file being transferred</param>
        void StartAction(BackendActionType action, string path, long size);
        /// <summary>
        /// Updates the current progress
        /// </summary>
        /// <param name="progress">he current number of transferred bytes</param>
        void UpdateProgress(long progress);
    }
    
    /// <summary>
    /// Combined interface for the backend progress updater and the backend progress item
    /// </summary>
    internal interface IBackendProgressUpdaterAndReporter : IBackendProgressUpdater, IBackendProgress
    {
    }
    
    /// <summary>
    /// Backend progress updater instance
    /// </summary>
    internal class BackendProgressUpdater : IBackendProgressUpdaterAndReporter
    {
        /// <summary>
        /// Lock object to provide snapshot-like access to the data
        /// </summary>
        private readonly object m_lock = new object();
        /// <summary>
        /// The current action
        /// </summary>
        private BackendActionType m_action;
        /// <summary>
        /// The current path
        /// </summary>
        private string m_path;
        /// <summary>
        /// The current file size
        /// </summary>
        private long m_size;
        /// <summary>
        /// The current number of transferred bytes
        /// </summary>
        private long m_progress;
        /// <summary>
        /// The time the last action started
        /// </summary>
        private DateTime m_actionStart;
        
        /// <summary>
        /// Register the start of a new action
        /// </summary>
        /// <param name="action">The action that is starting</param>
        /// <param name="path">The path being operated on</param>
        /// <param name="size">The size of the file being transferred</param>
        public void StartAction(BackendActionType action, string path, long size)
        {
            lock(m_lock)
            {
                m_action = action;
                m_path = path;
                m_size = size;
                m_progress = 0;
                m_actionStart = DateTime.Now;                
            }
        }
        
        /// <summary>
        /// Updates the progress
        /// </summary>
        /// <param name="progress">The current number of transferred bytes</param>
        public void UpdateProgress(long progress)
        {
            lock(m_lock)
                m_progress = progress;
        }
        
        /// <summary>
        /// Update with the current action, path, size, progress and bytes_pr_second.
        /// </summary>
        /// <param name="action">The current action</param>
        /// <param name="path">The current path</param>
        /// <param name="size">The current size</param>
        /// <param name="progress">The current number of transferred bytes</param>
        /// <param name="bytes_pr_second">Transfer speed in bytes pr second, -1 for unknown</param>
        public void Update(out BackendActionType action, out string path, out long size, out long progress, out long bytes_pr_second)
        {
            lock(m_lock)
            {
                action = m_action;
                path = m_path;
                size = m_size;
                progress = m_progress;
                
                //TODO: The speed should be more dynamic,
                // so we need a sample window instead of always 
                // calculating from the beginning
                if (m_progress <= 0 || m_size <= 0 || m_actionStart.Ticks == 0)
                    bytes_pr_second = -1;
                else
                    bytes_pr_second = (long)(m_progress / (DateTime.Now - m_actionStart).TotalSeconds);
            }
        }
        
    }
    
    public delegate void PhaseChangedDelegate(OperationPhase phase, OperationPhase previousPhase);
    
    /// <summary>
    /// Operation progress update object.
    /// The engine updates these statistics very often,
    /// so an event based system would take up too many resources.
    /// Instead, this interface allows the client to poll
    /// for updates as often as desired.
    /// </summary>
    public interface IOperationProgress
    {
        /// <summary>
        /// Update the phase, progress, filesprocessed, filesizeprocessed, filecount, filesize and countingfiles.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="progress">Progress.</param>
        /// <param name="filesprocessed">Filesprocessed.</param>
        /// <param name="filesizeprocessed">Filesizeprocessed.</param>
        /// <param name="filecount">Filecount.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="countingfiles">True if the filecount and filesize is incomplete, false otherwise</param>
        void UpdateOverall(out OperationPhase phase, out float progress, out long filesprocessed, out long filesizeprocessed, out long filecount, out long filesize, out bool countingfiles);
        
        /// <summary>
        /// Update the filename, filesize, and fileoffset.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="fileoffset">Fileoffset.</param>
        void UpdateFile(out string filename, out long filesize, out long fileoffset);
        
        /// <summary>
        /// Occurs when the phase has changed
        /// </summary>
        event PhaseChangedDelegate PhaseChanged;
    }
    
    /// <summary>
    /// Interface for updating the backend progress
    /// </summary>
    internal interface IOperationProgressUpdater
    {
        void UpdatePhase(OperationPhase phase);
        void UpdateProgress(float progress);
        void StartFile(string filename, long size);
        void UpdateFileProgress(long offset);
        void UpdatefileCount(long filecount, long filesize, bool done);
        void UpdatefilesProcessed(long count, long size);
    }
    
    internal interface IOperationProgressUpdaterAndReporter : IOperationProgressUpdater, IOperationProgress
    {
    }
    
    internal class OperationProgressUpdater : IOperationProgressUpdaterAndReporter
    {
        private readonly object m_lock = new object();
        
        private OperationPhase m_phase;
        private float m_progress;
        private string m_curfilename;
        private long m_curfilesize;
        private long m_curfileoffset;
        
        private long m_filesprocessed;
        private long m_filesizeprocessed;
        private long m_filecount;
        private long m_filesize;
        
        private bool m_countingFiles;
        
        public event PhaseChangedDelegate PhaseChanged;
        
        public void UpdatePhase(OperationPhase phase)
        {
            OperationPhase prev_phase;
            lock(m_lock)
            {
                prev_phase = m_phase;
                m_phase = phase;
                m_curfilename = null;
                m_curfilesize = 0;
                m_curfileoffset = 0;
            }
            
            if (prev_phase != phase && PhaseChanged != null)
                PhaseChanged(phase, prev_phase);
        }
        
        public void UpdateProgress(float progress)
        {
            lock(m_lock)
                m_progress = progress;
        }
        
        public void StartFile(string filename, long size)
        {
            lock(m_lock)
            {
                m_curfilename = filename;
                m_curfilesize = size;
                m_curfileoffset = 0;
            }
        }
        
        public void UpdateFileProgress(long offset)
        {
            lock(m_lock)
                m_curfileoffset = offset;
        }
        
        public void UpdatefileCount(long filecount, long filesize, bool done)
        {
            lock(m_lock)
            {
                m_filecount = filecount;
                m_filesize = filesize;
                m_countingFiles = !done;
            }
        }
        
        public void UpdatefilesProcessed(long count, long size)
        {
            lock(m_lock)
            {
                m_filesprocessed = count;
                m_filesizeprocessed = size;
            }
        }
        
        /// <summary>
        /// Update the phase, progress, filesprocessed, filesizeprocessed, filecount, filesize and countingfiles.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="progress">Progress.</param>
        /// <param name="filesprocessed">Filesprocessed.</param>
        /// <param name="filesizeprocessed">Filesizeprocessed.</param>
        /// <param name="filecount">Filecount.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="countingfiles">True if the filecount and filesize is incomplete, false otherwise</param>
        public void UpdateOverall(out OperationPhase phase, out float progress, out long filesprocessed, out long filesizeprocessed, out long filecount, out long filesize, out bool countingfiles)
        {
            lock(m_lock)
            {
                phase = m_phase;
                filesize = m_filesize;
                progress = m_progress;
                filesprocessed = m_filesprocessed;
                filesizeprocessed = m_filesizeprocessed;
                filecount = m_filecount;
                countingfiles = m_countingFiles;
            }
        }
        
        /// <summary>
        /// Update the filename, filesize, and fileoffset.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="fileoffset">Fileoffset.</param>
        public void UpdateFile(out string filename, out long filesize, out long fileoffset)
        {
            lock(m_lock)
            {
                filename = m_curfilename;
                filesize = m_curfilesize;
                fileoffset = m_curfileoffset;
            }
        }
    }
}

