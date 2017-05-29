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
using System.IO;
using Duplicati.Library.Main;

namespace Duplicati.CommandLine
{
    public class ConsoleOutput : Library.Main.IMessageSink
    {
        private object m_lock = new object();
        
        public bool QuietConsole { get; private set; }
        public bool VerboseOutput { get; private set; }
        public bool VerboseErrors { get; private set; }
        public TextWriter Output { get; private set; }
        
        public ConsoleOutput(TextWriter output, Dictionary<string, string> options)
        {
            this.Output = output;
            this.QuietConsole = Library.Utility.Utility.ParseBoolOption(options, "quiet-console");
            this.VerboseOutput = Library.Utility.Utility.ParseBoolOption(options, "verbose");
            this.VerboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");
        }
    
        #region IMessageSink implementation
        
        public IBackendProgress BackendProgress { get; set; }
        
        private IOperationProgress m_operationProgress;
        public IOperationProgress OperationProgress
        {
            get { return m_operationProgress; }
            set 
            { 
                if (m_operationProgress != null)
                    m_operationProgress.PhaseChanged -= InvokePhaseChanged;
                    
                m_operationProgress = value; 
                
                if (value != null)
                    m_operationProgress.PhaseChanged += InvokePhaseChanged;
            }
        }
        
        private void InvokePhaseChanged(OperationPhase p1, OperationPhase p2)
        {
            if (PhaseChanged != null)
                PhaseChanged(p1, p2);
        }
        
        public event PhaseChangedDelegate PhaseChanged;
        
        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            lock(m_lock)
                if (type == BackendEventType.Started)
                {
                    if (action == BackendActionType.Put)
                        Output.WriteLine("  Uploading file ({0}) ...", Library.Utility.Utility.FormatSizeString(size));
                    else if (action == BackendActionType.Get)
                        Output.WriteLine("  Downloading file ({0}) ...", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size));
                    else if (action == BackendActionType.List)
                        Output.WriteLine("  Listing remote folder ...");
                    else if (action == BackendActionType.CreateFolder)
                        Output.WriteLine("  Creating remote folder ...");
                    else if (action == BackendActionType.Delete)
                        Output.WriteLine("  Deleting file {0}{1} ...", path, size < 0 ? "" : (" (" + Library.Utility.Utility.FormatSizeString(size) + ")"));
                }
        }
                        
        public void VerboseEvent(string message, object[] args)
        {
            if (VerboseOutput)
                lock(m_lock)
                    Output.WriteLine(message, args);
        }
        public void MessageEvent(string message)
        {
            if (!QuietConsole)
                lock(m_lock)
                    Output.WriteLine(message);
        }
        
        public void RetryEvent(string message, Exception ex)
        {
            if (!QuietConsole)
                lock(m_lock)
                    Output.WriteLine(ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message));
        }
        public void WarningEvent(string message, Exception ex)
        {
            if (!QuietConsole)
                lock(m_lock)
                    Output.WriteLine(ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message));
        }
        public void ErrorEvent(string message, Exception ex)
        {
            if (!QuietConsole)
                lock(m_lock)
                    Output.WriteLine(ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message));
        }
        public void DryrunEvent(string message)
        {
            if (!QuietConsole)
                lock(m_lock)
                    Output.WriteLine(string.Format("[Dryrun]: {0}", message));
        }
        #endregion
    }
}

