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
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Handles writing incomming messages to the current log interface
    /// </summary>
    internal static class LogHandler
    {
        public static Task Run(ILogWriter log)
        {
            return AutomationExtensions.RunTask(
                new
                {
                    LogChannel = ChannelManager.GetChannel(Channels.LogChannel.ForRead)
                },

                async self =>
                {
                    while(true)
                    {
                        var msg = await self.LogChannel.ReadAsync();

                        if (msg.IsDryRun)
                            log.AddDryrunMessage(msg.Message);
                        else if (msg.IsVerbose)
                            log.AddVerboseMessage(msg.Message);
                        else if (msg.IsRetry && log is IBackendWriter)
                            ((IBackendWriter)log).AddRetryAttempt(msg.Message, msg.Exception);
                        else if (msg.Level == Duplicati.Library.Logging.LogMessageType.Error)
                            log.AddError(msg.Message, msg.Exception);
                        else if (msg.Level == Duplicati.Library.Logging.LogMessageType.Warning)
                            log.AddWarning(msg.Message, msg.Exception);
                        else if (msg.Level == Duplicati.Library.Logging.LogMessageType.Profiling)
                            log.AddVerboseMessage(msg.Message);
                        else
                            log.AddMessage(msg.Message);

                    }
                }
            );
        }
    }
}

