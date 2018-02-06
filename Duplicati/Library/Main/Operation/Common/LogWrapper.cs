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
using CoCoL;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Helper method to reduce code-text size when writing log messages
    /// </summary>
    public class LogWrapper : IDisposable
    {
        private bool m_autoLeave;
        private IWriteChannel<LogMessage> m_channel;

        //TODO: Fix this so the verbose messages are not sent unless required

        public LogWrapper()
        {
            m_channel = ChannelManager.GetChannel(Channels.LogChannel.ForWrite);
            m_autoLeave = true;
        }

        public LogWrapper(IWriteChannel<LogMessage> channel)
        {
            m_channel = channel;
            m_autoLeave = false;
        }

        public Task WriteWarningAsync(string message, Exception ex)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Warning,
                Message = message,
                Exception = ex
            });
        }

        public Task WriteErrorAsync(string message, Exception ex)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Error,
                Message = message,
                Exception = ex
            });
        }

        public Task WriteProfilingAsync(string message, Exception ex)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Profiling,
                Message = message,
                Exception = ex
            });
        }

        public Task WriteInformationAsync(string message, Exception ex = null)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = message,
                Exception = ex
            });
        }

        public Task WriteVerboseAsync(string message, params object[] args)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsVerbose = true
            });
        }

        public Task WriteDryRunAsync(string message, params object[] args)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsDryRun = true
            });
        }

        public Task WriteRetryAttemptAsync(string message, params object[] args)
        {
            return m_channel.WriteAsync(new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsRetry = true
            });
        }

        public void Dispose()
        {
            if (m_autoLeave && m_channel != null)
            {
                m_autoLeave = false;
                if (m_channel is IJoinAbleChannel)
                    ((IJoinAbleChannel)m_channel).Leave(false);
                else if (m_channel is IJoinAbleChannelEnd)
                    ((IJoinAbleChannelEnd)m_channel).Leave();
            }
            m_channel = null;
        }
    }
}

