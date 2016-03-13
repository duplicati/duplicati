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

namespace Duplicati.Library.UsageReporter
{
    /// <summary>
    /// The usage reporter library interface
    /// </summary>
    public static class Reporter
    {
        /// <summary>
        /// The primary input channel for new report messages
        /// </summary>
        private static IWriteChannel<ReportItem> _eventChannel;

        /// <summary>
        /// The task to await before shutdown
        /// </summary>
        private static Task ShutdownTask;

        /// <summary>
        /// Reports an event, information by default
        /// </summary>
        /// <param name="key">The event name</param>
        /// <param name="data">The event data</param>
        /// <param name="type">The event type</param>
        public static void Report(string key, string data = null, ReportType type = ReportType.Information)
        {
            if (_eventChannel != null && type >= MaxReportLevel)
                try { _eventChannel.TryWrite(new ReportItem(type, null, key, data)); }
                catch { }
        }

        /// <summary>
        /// Reports an event, information by default
        /// </summary>
        /// <param name="key">The event name</param>
        /// <param name="count">The event count</param>
        /// <param name="type">The event type</param>
        public static void Report(string key, long count, ReportType type = ReportType.Information)
        {
            if (_eventChannel != null && type >= MaxReportLevel)
                try { _eventChannel.TryWrite(new ReportItem(type, count, key, count.ToString())); }
                catch { }
        }

        /// <summary>
        /// Reports an exception event, error by default
        /// </summary>
        /// <param name="key">The event name</param>
        /// <param name="count">The event count</param>
        /// <param name="type">The event type</param>
        public static void Report(Exception ex, ReportType type = ReportType.Warning)
        {
            if (_eventChannel != null && type >= MaxReportLevel)
                try { _eventChannel.TryWrite(new ReportItem(type, null, "EXCEPTION", ex.ToString())); }
                catch { }
        }

        /// <summary>
        /// Initializes the usage reporter library
        /// </summary>
        public static void Initialize()
        {
            if (_eventChannel == null || _eventChannel.IsRetired)
            {
                if (IsDisabled)
                    return;

                var rsu = new ReportSetUploader();
                var ep = new EventProcessor(rsu.Channel);
                _eventChannel = ep.Channel;

                ShutdownTask = Task.WhenAll(ep.Terminated, rsu.Terminated);

                // TODO: Disable on debug builds
                AppDomain.CurrentDomain.UnhandledException += HandleUncaughtException;
                //AppDomain.CurrentDomain.UnhandledException += HandleUncaughtException;
                //AppDomain.CurrentDomain.ProcessExit

                Report("Started");
            }
        }


        /// <summary>
        /// Handles an uncaught exception.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
        private static void HandleUncaughtException(object sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception)
                Report(args.ExceptionObject as Exception, ReportType.Crash);
        }

        /// <summary>
        /// Terminates the usage reporter library
        /// </summary>
        public static void ShutDown()
        {
            if (_eventChannel != null && !_eventChannel.IsRetired)
                _eventChannel.Retire();

            if (ShutdownTask != null)
                ShutdownTask.Wait();

            AppDomain.CurrentDomain.UnhandledException -= HandleUncaughtException;

        }

        /// <summary>
        /// Allow opt-out
        /// </summary>
        internal const string DISABLED_ENVNAME_TEMPLATE = "USAGEREPORTER_{0}_LEVEL";

        /// <summary>
        /// Cached value with the max report level
        /// </summary>
        private static ReportType? Cached_MaxReportLevel;

        /// <summary>
        /// A value indicating if the usage reporter library is forced disabled
        /// </summary>
        private static bool Forced_Disabled = false;

        /// <summary>
        /// Gets the environment default report level
        /// </summary>
        /// <value>The system default report level.</value>
        public static string DefaultReportLevel
        {
            get
            {
                return IsDisabledByEnvironment ? "Disabled" : MaxReportLevel.ToString();
            }
        }

        /// <summary>
        /// The maxmimum allowed report level
        /// </summary>
        /// <value>The type of the max report.</value>
        private static ReportType MaxReportLevel
        {
            get
            {
                if (Cached_MaxReportLevel == null)
                {
                    var str = Environment.GetEnvironmentVariable(string.Format(DISABLED_ENVNAME_TEMPLATE, AutoUpdater.AutoUpdateSettings.AppName));
                    ReportType tmp;
                    if (string.IsNullOrWhiteSpace(str) || !Enum.TryParse(str, out tmp))
                        Cached_MaxReportLevel = ReportType.Information;
                    else
                        Cached_MaxReportLevel = tmp;
                }

                return Cached_MaxReportLevel.Value;
            }
        }

        /// <summary>
        /// Gets a value indicating if the user has opted out of usage reporting
        /// </summary>
        /// <value><c>true</c> if is disabled; otherwise, <c>false</c>.</value>
        private static bool IsDisabled
        {
            get 
            {
                if (Forced_Disabled)
                    return true;

                return IsDisabledByEnvironment;
            }
        }

        /// <summary>
        /// Gets a value indicating if the user has opted out of usage reporting,
        /// but without reading the local override option
        /// </summary>
        /// <value><c>true</c> if is disabled; otherwise, <c>false</c>.</value>
        private static bool IsDisabledByEnvironment
        {
            get
            {
                var str = Environment.GetEnvironmentVariable(string.Format(DISABLED_ENVNAME_TEMPLATE, AutoUpdater.AutoUpdateSettings.AppName));
#if DEBUG
                // Default to not report crashes etc from debug builds
                if (string.IsNullOrWhiteSpace(str))
                    str = "none";
#endif
                return string.Equals(str, "none", StringComparison.InvariantCultureIgnoreCase) || Utility.Utility.ParseBool(str, false);
            }
        }

        /// <summary>
        /// Sets the usage reporter level
        /// </summary>
        /// <param name="maxreportlevel">The maximum level of events to report, or null to set default.</param>
        /// <param name="disable"><c>True</c> to disable usage reporting<c>false</c> otherwise.</param>
        public static void SetReportLevel(ReportType? maxreportlevel, bool disable)
        {
            if (disable)
            {
                Forced_Disabled = true;
                ShutDown();
            }
            else
            {
                Forced_Disabled = false;
                Cached_MaxReportLevel = maxreportlevel;
                Initialize();
            }
        }
    }
}

