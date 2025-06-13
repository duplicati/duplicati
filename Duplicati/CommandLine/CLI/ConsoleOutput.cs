// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

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

        public IOperationProgress OperationProgress { get; private set; }

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
                            Output.WriteLine("  Uploading file {0} ({1}) ...", path, Library.Utility.Utility.FormatSizeString(size));
                            break;
                        case BackendActionType.Get:
                            Output.WriteLine("  Downloading file {0} ({1}) ...", path, size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size));
                            break;
                        case BackendActionType.List:
                            Output.WriteLine("  Listing remote folder {0}...", path);
                            break;
                        case BackendActionType.CreateFolder:
                            Output.WriteLine("  Creating remote folder {0} ...", path);
                            break;
                        case BackendActionType.Delete:
                            Output.WriteLine("  Deleting file {0} {1} ...", path, size < 0 ? "" : (" (" + Library.Utility.Utility.FormatSizeString(size) + ")"));
                            break;
                    }
                }
        }

        public void SetBackendProgress(IBackendProgress progress)
        {
            // Do nothing. Implementation needed for IMessageSink interface.
        }

        public void SetOperationProgress(IOperationProgress progress)
        {
            if (OperationProgress != null)
                this.OperationProgress.PhaseChanged -= InvokePhaseChanged;

            OperationProgress = progress;

            if (progress != null)
                this.OperationProgress.PhaseChanged += InvokePhaseChanged;
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

