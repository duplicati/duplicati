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
    public class ConsoleOutput : Library.Main.IMessageSink, IDisposable
    {
        private readonly object m_lock = new object();
        
        public bool QuietConsole { get; private set; }
        public bool VerboseErrors { get; private set; }
        public TextWriter Output { get; private set; }
        public bool FullResults { get; private set; }
        
        public ConsoleOutput(TextWriter output, Dictionary<string, string> options)
        {
            this.Output = output;
            this.QuietConsole = Library.Utility.Utility.ParseBoolOption(options, "quiet-console");
            this.VerboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");
            this.FullResults = Library.Utility.Utility.ParseBoolOption(options, "full-results");
        }
    
        #region IMessageSink implementation
       
        private IOperationProgress m_operationProgress;
        public IOperationProgress OperationProgress => m_operationProgress;

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
                    switch (action)
                    {
                        case BackendActionType.Put:
                            Output.WriteLine("  Uploading file ({0}) ...", Library.Utility.Utility.FormatSizeString(size));
                            break;
                        case BackendActionType.Get:
                            Output.WriteLine("  Downloading file ({0}) ...", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size));
                            break;
                        case BackendActionType.List:
                            Output.WriteLine("  Listing remote folder ...");
                            break;
                        case BackendActionType.CreateFolder:
                            Output.WriteLine("  Creating remote folder ...");
                            break;
                        case BackendActionType.Delete:
                            Output.WriteLine("  Deleting file {0}{1} ...", path, size < 0 ? "" : (" (" + Library.Utility.Utility.FormatSizeString(size) + ")"));
                            break;
                    }
                }
        }

        public void SetBackendProgress(IBackendProgress progress)
        {
            // Not implemented.
        }

        public void SetOperationProgress(IOperationProgress progress)
        {
            if (m_operationProgress != null)
                m_operationProgress.PhaseChanged -= InvokePhaseChanged;

            m_operationProgress = progress;

            if (progress != null)
                m_operationProgress.PhaseChanged += InvokePhaseChanged;
        }

        public void WriteMessage(Library.Logging.LogEntry entry)
        {
            if (QuietConsole)
                return;
                
            lock (m_lock)
            {
                if (entry.Exception != null)
                    Output.WriteLine("{0} => {1}", entry.FormattedMessage, VerboseErrors ? entry.Exception.ToString() : entry.Exception.Message);
                else if (entry.Level == Library.Logging.LogMessageType.DryRun)
                    Output.WriteLine("[Dryrun]: {0}", entry.FormattedMessage);

                else
                    Output.WriteLine(entry.FormattedMessage);
            }

        }

        public void MessageEvent(string message)
        {
            if (QuietConsole)
                return;
                
            lock (m_lock)
                Output.WriteLine(message);

        }

        public void Dispose()
        {
        }
        #endregion
    }
}

