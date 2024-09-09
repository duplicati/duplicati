// Copyright (C) 2024, The Duplicati Team
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
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(Reporter));
        
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
                try { _eventChannel.WriteNoWait(new ReportItem(type, null, key, data)); }
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
                try { _eventChannel.WriteNoWait(new ReportItem(type, count, key, count.ToString())); }
                catch { }
        }

        /// <summary>
        /// Reports an exception event, error by default
        /// </summary>
        /// <param name="ex">The exception</param>
        /// <param name="type">The event type</param>
        public static void Report(Exception ex, ReportType type = ReportType.Warning)
        {
            if (_eventChannel != null && type >= MaxReportLevel)
                try { _eventChannel.WriteNoWait(new ReportItem(type, null, "EXCEPTION", ex.ToString())); }
                catch { }
        }

        /// <summary>
        /// Initializes the usage reporter library
        /// </summary>
        public static void Initialize()
        {
            if (_eventChannel == null || _eventChannel.IsRetiredAsync.Result)
            {
                if (IsDisabled)
                    return;

                var rsu = ReportSetUploader.Run();
                var ep = EventProcessor.Run(rsu.Item2);
                _eventChannel = ep.Item2;

                ShutdownTask = Task.WhenAll(ep.Item1, rsu.Item1);

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
            if (args.ExceptionObject is Exception exception)
                Report(exception, ReportType.Crash);
        }

        /// <summary>
        /// Terminates the usage reporter library
        /// </summary>
        public static void ShutDown()
        {
            if (_eventChannel != null && !_eventChannel.IsRetiredAsync.Result)
                _eventChannel.Retire();

            if (ShutdownTask != null)
            {
                ShutdownTask.Wait(TimeSpan.FromSeconds(30));
                if (!ShutdownTask.IsCompleted)
                    Logging.Log.WriteWarningMessage(LOGTAG, "ReporterShutdownFailuer", null, "Failed to shut down usage reporter after 30 seconds, leaving hanging ...");
            }

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
        /// The maximum allowed report level
        /// </summary>
        /// <value>The type of the max report.</value>
        private static ReportType MaxReportLevel
        {
            get
            {
                if (Cached_MaxReportLevel == null)
                {
                    var str = Environment.GetEnvironmentVariable(string.Format(DISABLED_ENVNAME_TEMPLATE, Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName));
                    ReportType tmp;
                    if (string.IsNullOrWhiteSpace(str) || !Enum.TryParse(str, true, out tmp))
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
                return string.Equals(str, "none", StringComparison.OrdinalIgnoreCase) || Utility.Utility.ParseBool(str, false);
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

