#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
using System.Linq;


#endregion
using System;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;

namespace Duplicati.Library.Main
{
    public class Controller : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Controller>();
        /// <summary>
        /// The backend url
        /// </summary>
        private string m_backend;
        /// <summary>
        /// The parsed type-safe version of the commandline options
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The destination for all output messages during execution
        /// </summary>
        private IMessageSink m_messageSink;

        /// <summary>
        /// The current executing task
        /// </summary>
        private ITaskControl m_currentTask = null;

        /// <summary>
        /// The thread running the current task
        /// </summary>
        private System.Threading.Thread m_currentTaskThread = null;

        /// <summary>
        /// The thread priority to reset to
        /// </summary>
        private System.Threading.ThreadPriority? m_resetPriority;

        /// <summary>
        /// The localization culture to reset to
        /// </summary>
        private System.Globalization.CultureInfo m_resetLocale;

        /// <summary>
        /// The localization UI culture to reset to
        /// </summary>
        private System.Globalization.CultureInfo m_resetLocaleUI;

        /// <summary>
        /// True if the locale should be reset
        /// </summary>
        private bool m_doResetLocale;

        /// <summary>
        /// The multi-controller log target
        /// </summary>
        private ControllerMultiLogTarget m_logTarget;

        /// <summary>
        /// Constructs a new interface for performing backup and restore operations
        /// </summary>
        /// <param name="backend">The url for the backend to use</param>
        /// <param name="options">All required options</param>
        public Controller(string backend, Dictionary<string, string> options, IMessageSink messageSink)
        {
            m_backend = backend;
            m_options = new Options(options);
            m_messageSink = messageSink;
        }

        /// <summary>
        /// Appends another message sink to the controller
        /// </summary>
        /// <param name="sink">The sink to use.</param>
        public void AppendSink(IMessageSink sink)
        {
            if (m_messageSink is MultiMessageSink)
                ((MultiMessageSink)m_messageSink).Append(sink);
            else
                m_messageSink = new MultiMessageSink(m_messageSink, sink);
        }

        public Duplicati.Library.Interface.IBackupResults Backup(string[] inputsources, IFilter filter = null)
        {
            Library.UsageReporter.Reporter.Report("USE_BACKEND", new Library.Utility.Uri(m_backend).Scheme);
            Library.UsageReporter.Reporter.Report("USE_COMPRESSION", m_options.CompressionModule);
            Library.UsageReporter.Reporter.Report("USE_ENCRYPTION", m_options.EncryptionModule);

            CheckAutoCompactInterval();
            CheckAutoVacuumInterval();

            return RunAction(new BackupResults(), ref inputsources, ref filter, (result) => {

                using (var h = new Operation.BackupHandler(m_backend, m_options, result))
                    h.Run(ExpandInputSources(inputsources, filter), filter);

                Library.UsageReporter.Reporter.Report("BACKUP_FILECOUNT", result.ExaminedFiles);
                Library.UsageReporter.Reporter.Report("BACKUP_FILESIZE", result.SizeOfExaminedFiles);
                Library.UsageReporter.Reporter.Report("BACKUP_DURATION", (long)result.Duration.TotalSeconds);
            });
        }

        public Library.Interface.IRestoreResults Restore(string[] paths, Library.Utility.IFilter filter = null)
        {
            return RunAction(new RestoreResults(), ref paths, ref filter, (result) => {
                new Operation.RestoreHandler(m_backend, m_options, result).Run(paths, filter);

                Library.UsageReporter.Reporter.Report("RESTORE_FILECOUNT", result.RestoredFiles);
                Library.UsageReporter.Reporter.Report("RESTORE_FILESIZE", result.SizeOfRestoredFiles);
                Library.UsageReporter.Reporter.Report("RESTORE_DURATION", (long)result.Duration.TotalSeconds);
            });
        }

        public Duplicati.Library.Interface.IRestoreControlFilesResults RestoreControlFiles(IEnumerable<string> files = null, Library.Utility.IFilter filter = null)
        {
            return RunAction(new RestoreControlFilesResults(), ref filter, (result) => {
                new Operation.RestoreControlFilesHandler(m_backend, m_options, result).Run(files, filter);
            });
        }

        public Duplicati.Library.Interface.IDeleteResults Delete()
        {
            return RunAction(new DeleteResults(), (result) => {
                new Operation.DeleteHandler(m_backend, m_options, result).Run();
            });
        }

        public Duplicati.Library.Interface.IRepairResults Repair(Library.Utility.IFilter filter = null)
        {
            return RunAction(new RepairResults(), ref filter, (result) => {
                new Operation.RepairHandler(m_backend, m_options, result).Run(filter);
            });
        }

        public Duplicati.Library.Interface.IListResults List()
        {
            return List(null, null);
        }

        public Duplicati.Library.Interface.IListResults List(string filterstring)
        {
            return List(filterstring == null ? null : new string[] { filterstring }, null);
        }

        public Duplicati.Library.Interface.IListResults List(IEnumerable<string> filterstrings, Library.Utility.IFilter filter)
        {
            return RunAction(new ListResults(), ref filter, (result) => {
                new Operation.ListFilesHandler(m_backend, m_options, result).Run(filterstrings, filter);
            });
        }

        public Duplicati.Library.Interface.IListResults ListControlFiles(IEnumerable<string> filterstrings, Library.Utility.IFilter filter)
        {
            return RunAction(new ListResults(), ref filter, (result) => {
                new Operation.ListControlFilesHandler(m_backend, m_options, result).Run(filterstrings, filter);
            });
        }

        public Duplicati.Library.Interface.IListRemoteResults ListRemote()
        {
            return RunAction(new ListRemoteResults(), (result) =>
            {
                using (var tf = System.IO.File.Exists(m_options.Dbpath) ? null : new Library.Utility.TempFile())
                using (var db = new Database.LocalDatabase(((string)tf) ?? m_options.Dbpath, "list-remote", true))
                using (var bk = new BackendManager(m_backend, m_options, result.BackendWriter, null))
                    result.SetResult(bk.List());
            });
        }

        public Duplicati.Library.Interface.IListRemoteResults DeleteAllRemoteFiles()
        {
            return RunAction(new ListRemoteResults(), (result) =>
            {
                result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Listing);
                using (var tf = System.IO.File.Exists(m_options.Dbpath) ? null : new Library.Utility.TempFile())
                using (var db = new Database.LocalDatabase(((string)tf) ?? m_options.Dbpath, "list-remote", true))
                using (var bk = new BackendManager(m_backend, m_options, result.BackendWriter, null))
                {
                    // Only delete files that match the expected pattern and prefix
                    var list = bk.List()
                        .Select(x => Volumes.VolumeBase.ParseFilename(x))
                        .Where(x => x != null)
                        .Where(x => x.Prefix == m_options.Prefix)
                        .ToList();

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Deleting);
                    result.OperationProgressUpdater.UpdateProgress(0);
                    for (var i = 0; i < list.Count; i++)
                    {
                        try
                        {
                            bk.Delete(list[i].File.Name, list[i].File.Size, true);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFilesetError", ex, "Failed to delete remote file: {0}", list[i].File.Name);
                        }
                        result.OperationProgressUpdater.UpdateProgress((float)i / list.Count);
                    }
                    result.OperationProgressUpdater.UpdateProgress(1);
                }
            });
        }

        public Duplicati.Library.Interface.ICompactResults Compact()
        {
            CheckAutoVacuumInterval();

            return RunAction(new CompactResults(), (result) => {
                new Operation.CompactHandler(m_backend, m_options, result).Run();
            });
        }

        public Duplicati.Library.Interface.IRecreateDatabaseResults UpdateDatabaseWithVersions(Library.Utility.IFilter filter = null)
        {
            var filelistfilter = Operation.RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version, singleTimeMatch: true);

            return RunAction(new RecreateDatabaseResults(), ref filter, (result) => {
                using(var h = new Operation.RecreateDatabaseHandler(m_backend, m_options, result))
                    h.RunUpdate(filter, filelistfilter);
            });
        }

        public Duplicati.Library.Interface.ICreateLogDatabaseResults CreateLogDatabase(string targetpath)
        {
            var t = new string[] { targetpath };

            return RunAction(new CreateLogDatabaseResults(), ref t, (result) => {
                new Operation.CreateBugReportHandler(t[0], m_options, result).Run();
            });
        }

        public Duplicati.Library.Interface.IListChangesResults ListChanges(string baseVersion, string targetVersion, IEnumerable<string> filterstrings = null, Library.Utility.IFilter filter = null, Action<Duplicati.Library.Interface.IListChangesResults, IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>>> callback = null)
        {
            var t = new string[] { baseVersion, targetVersion };

            return RunAction(new ListChangesResults(), ref t, ref filter, (result) => {
                new Operation.ListChangesHandler(m_backend, m_options, result).Run(t[0], t[1], filterstrings, filter, callback);
            });
        }

        public Duplicati.Library.Interface.IListAffectedResults ListAffected(List<string> args, Action<Duplicati.Library.Interface.IListAffectedResults> callback = null)
        {
            return RunAction(new ListAffectedResults(), (result) => {
                new Operation.ListAffected(m_options, result).Run(args, callback);
            });
        }

        public Duplicati.Library.Interface.ITestResults Test(long samples = 1)
        {
            if (!m_options.RawOptions.ContainsKey("full-remote-verification"))
                m_options.RawOptions["full-remote-verification"] = "true";
                
            return RunAction(new TestResults(), (result) => {
                new Operation.TestHandler(m_backend, m_options, result).Run(samples);
            });
        }

        public Library.Interface.ITestFilterResults TestFilter(string[] paths, Library.Utility.IFilter filter = null)
        {
            m_options.RawOptions["dry-run"] = "true";
            m_options.RawOptions["dbpath"] = "INVALID!";

            // Redirect all messages from the filter to the message sink
            var filtertag = Logging.Log.LogTagFromType(typeof(Operation.Backup.FileEnumerationProcess));
            using (Logging.Log.StartScope(m_messageSink.WriteMessage, x => x.FilterTag.Contains(filtertag)))
            {
                return RunAction(new TestFilterResults(), ref paths, ref filter, (result) =>
                {
                    new Operation.TestFilterHandler(m_options, result).Run(ExpandInputSources(paths, filter), filter);
                });
            }
        }

        public Library.Interface.ISystemInfoResults SystemInfo()
        {
            return RunAction(new SystemInfoResults(), result => {
                Operation.SystemInfoHandler.Run(result);
            });
        }

        public Library.Interface.IPurgeFilesResults PurgeFiles(Library.Utility.IFilter filter)
        {
            return RunAction(new PurgeFilesResults(), result =>
            {
                new Operation.PurgeFilesHandler(m_backend, m_options, result).Run(filter);
            });
        }

        public Library.Interface.IListBrokenFilesResults ListBrokenFiles(Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler = null)
        {
            return RunAction(new ListBrokenFilesResults(), result =>
            {
                new Operation.ListBrokenFilesHandler(m_backend, m_options, result).Run(filter, callbackhandler);
            });
        }

        public Library.Interface.IPurgeBrokenFilesResults PurgeBrokenFiles(Library.Utility.IFilter filter)
        {
            return RunAction(new PurgeBrokenFilesResults(), result =>
            {
                new Operation.PurgeBrokenFilesHandler(m_backend, m_options, result).Run(filter);
            });
        }

        public Library.Interface.ISendMailResults SendMail()
        {
            m_options.RawOptions["send-mail-level"] = "all";
            m_options.RawOptions["send-mail-any-operation"] = "true";
            string targetmail;
            m_options.RawOptions.TryGetValue("send-mail-to", out targetmail);
            if (string.IsNullOrWhiteSpace(targetmail))
                throw new Exception(string.Format("No email specified, please use --{0}", "send-mail-to"));

            m_options.RawOptions["disable-module"] = string.Join(
                ",",
                DynamicLoader.GenericLoader.Modules
                         .Where(m =>
                                !(m is Modules.Builtin.SendMail)
                         )
                .Select(x => x.Key)
            );
                         
            /// Forward all messages from the email module to the message sink
            var filtertag = Logging.Log.LogTagFromType<Modules.Builtin.SendMail>();
            using (Logging.Log.StartScope(m_messageSink.WriteMessage, x => x.FilterTag.Contains(filtertag)))
            {
                return RunAction(new SendMailResults(), result =>
                {
                    result.Lines = new string[0];
                    System.Threading.Thread.Sleep(5);
                });
            }
        }

        public Library.Interface.IVacuumResults Vacuum()
        {
            return RunAction(new VacuumResults(), result => {
                new Operation.VacuumHandler(m_options, result).Run();
            });
        }

        private T RunAction<T>(T result, Action<T> method)
            where T : ISetCommonOptions, ITaskControl, Logging.ILogDestination
        {
            var tmp = new string[0];
            IFilter tempfilter = null;
            return RunAction<T>(result, ref tmp, ref tempfilter, method);
        }

        private T RunAction<T>(T result, ref string[] paths, Action<T> method)
            where T : ISetCommonOptions, ITaskControl, Logging.ILogDestination
        {
            IFilter tempfilter = null;
            return RunAction<T>(result, ref paths, ref tempfilter, method);
        }

        private T RunAction<T>(T result, ref IFilter filter, Action<T> method)
            where T : ISetCommonOptions, ITaskControl, Logging.ILogDestination
        {
            var tmp = new string[0];
            return RunAction<T>(result, ref tmp, ref filter, method);
        }

        private T RunAction<T>(T result, ref string[] paths, ref IFilter filter, Action<T> method)
            where T : ISetCommonOptions, ITaskControl, Logging.ILogDestination
        {
            m_logTarget = new ControllerMultiLogTarget(result, Logging.LogMessageType.Information, null);
            using (Logging.Log.StartScope(m_logTarget, null))
            {
                m_logTarget.AddTarget(m_messageSink, m_options.ConsoleLoglevel, m_options.ConsoleLogFilter);
                result.MessageSink = m_messageSink;

                try
                {
                    m_currentTask = result;
                    m_currentTaskThread = System.Threading.Thread.CurrentThread;

                    SetupCommonOptions(result, ref paths, ref filter);
                    Logging.Log.WriteInformationMessage(LOGTAG, "StartingOperation", Strings.Controller.StartingOperationMessage(m_options.MainAction));

                    using (new ProcessController(m_options))
                    using (new Logging.Timer(LOGTAG, string.Format("Run{0}", result.MainOperation), string.Format("Running {0}", result.MainOperation)))
                    using(new CoCoL.IsolatedChannelScope())
                    using(m_options.ConcurrencyMaxThreads <= 0 ? null : new CoCoL.CappedThreadedThreadPool(m_options.ConcurrencyMaxThreads))
                        method(result);

                    if (result.EndTime.Ticks == 0)
                        result.EndTime = DateTime.UtcNow;
                    result.SetDatabase(null);

                    OnOperationComplete(result);

                    Logging.Log.WriteInformationMessage(LOGTAG, "CompletedOperation", Strings.Controller.CompletedOperationMessage(m_options.MainAction));

                    return result;
                }
                catch (Exception ex)
                {
                    result.EndTime = DateTime.UtcNow;

                    if (ex is Library.Interface.OperationAbortException oae)
                    {
                        // Perform the module shutdown
                        OnOperationComplete(ex);

                        // Log this as a normal operation, as the script raising the exception,
                        // has already populated either warning or log messages as required
                        Logging.Log.WriteInformationMessage(LOGTAG, "AbortOperation", "Aborting operation by request, requested result: {0}", oae.AbortReason);

                        return result;
                    }
                    else
                    {
                        try { (result as BasicResults).OperationProgressUpdater.UpdatePhase(OperationPhase.Error); }
                        catch { }

                        OnOperationComplete(ex);

                        Logging.Log.WriteErrorMessage(LOGTAG, "FailedOperation", ex, Strings.Controller.FailedOperationMessage(m_options.MainAction, ex.Message));

                        throw;
                    }

                }
                finally
                {
                    m_currentTask = null;
                    m_currentTaskThread = null;
                }
            }
        }

        /// <summary>
        /// Attempts to get the locale, but delays linking to the calls as they are missing in some environments
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoGetLocale(out System.Globalization.CultureInfo locale, out System.Globalization.CultureInfo uiLocale)
        {
            locale = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
            uiLocale = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        }

        /// <summary>
        /// Attempts to set the locale, but delays linking to the calls as they are missing in some environments
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoSetLocale(System.Globalization.CultureInfo locale, System.Globalization.CultureInfo uiLocale)
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = locale;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = uiLocale;
        }

        private void OnOperationComplete(object result)
        {
            if (m_options != null && m_options.LoadedModules != null)
            {
                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is Duplicati.Library.Interface.IGenericCallbackModule)
                        try { ((Duplicati.Library.Interface.IGenericCallbackModule)mx.Value).OnFinish(result); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"OnFinishError{mx.Key}", ex, "OnFinish callback {0} failed: {1}", mx.Key, ex.Message); }

                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is IDisposable)
                        try { ((IDisposable)mx.Value).Dispose(); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"DisposeError{mx.Key}", ex, "Dispose for {0} failed: {1}", mx.Key, ex.Message); }

                m_options.LoadedModules.Clear();
            }

            if (m_resetPriority != null)
            {
                System.Threading.Thread.CurrentThread.Priority = m_resetPriority.Value;
                m_resetPriority = null;
            }

            if (m_doResetLocale)
            {
                // Wrap the call to avoid loading issues for the setLocale method
                DoSetLocale(m_resetLocale, m_resetLocaleUI);

                m_doResetLocale = false;
                m_resetLocale = null;
                m_resetLocaleUI = null;
            }

            if (m_logTarget != null)
            {
                m_logTarget.Dispose();
                m_logTarget = null;
            }
        }

        private void SetupCommonOptions(ISetCommonOptions result, ref string[] paths, ref IFilter filter)
        {
            m_options.MainAction = result.MainOperation;

            switch (m_options.MainAction)
            {
                case OperationMode.Backup:
                    break;

                default:
                    //It only makes sense to enable auto-creation if we are writing files.
                    if (!m_options.RawOptions.ContainsKey("disable-autocreate-folder"))
                        m_options.RawOptions["disable-autocreate-folder"] = "true";
                    break;
            }

            //Load all generic modules
            m_options.LoadedModules.Clear();

            foreach (Library.Interface.IGenericModule m in DynamicLoader.GenericLoader.Modules)
                m_options.LoadedModules.Add(new KeyValuePair<bool, Library.Interface.IGenericModule>(!m_options.DisableModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase) && (m.LoadAsDefault || m_options.EnableModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase)), m));

            // Make the filter read-n-write able in the generic modules
            var pristinefilter = string.Join(System.IO.Path.PathSeparator.ToString(), FilterExpression.Serialize(filter));
            m_options.RawOptions["filter"] = pristinefilter;
            
            // Store the URL connection options separately, as these should only be visible to modules implementing IConnectionModule
            var conopts = new Dictionary<string, string>(m_options.RawOptions);
            var qp = new Library.Utility.Uri(m_backend).QueryParameters;
            foreach (var k in qp.Keys)
                conopts[(string)k] = qp[(string)k];

            //// Since Configure in RunScript can alter the RawOptions, make sure it is first in the list for Configure
            var LoadedModules = new List<KeyValuePair<bool, Interface.IGenericModule>>();
            foreach (var mx in m_options.LoadedModules)
                if (mx.Value.ToString().IndexOf("runscript", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LoadedModules.Insert(0, mx);
                }
                else
                {
                    LoadedModules.Add(mx);
                }

            foreach (var mx in LoadedModules)
                if (mx.Key)
                {
                    if (mx.Value is Library.Interface.IConnectionModule)
                        mx.Value.Configure(conopts);
                    else
                        mx.Value.Configure(m_options.RawOptions);

                    if (mx.Value is Library.Interface.IGenericSourceModule)
                    {
                        var sourcemodule = (Library.Interface.IGenericSourceModule)mx.Value;

                        if (sourcemodule.ContainFilesForBackup(paths))
                        {
                            var sourceoptions = sourcemodule.ParseSourcePaths(ref paths, ref pristinefilter, m_options.RawOptions);

                            foreach (var sourceoption in sourceoptions)
                                m_options.RawOptions[sourceoption.Key] = sourceoption.Value;
                        }
                    }

                    if (mx.Value is Library.Interface.IGenericCallbackModule)
                        ((Library.Interface.IGenericCallbackModule)mx.Value).OnStart(result.MainOperation.ToString(), ref m_backend, ref paths);
                }

            // If the filters were changed by a module, read them back in
            if (pristinefilter != m_options.RawOptions["filter"])
            {
                filter = FilterExpression.Deserialize(m_options.RawOptions["filter"].Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));
            }
            m_options.RawOptions.Remove("filter"); // "--filter" is not a supported command line option

            if (!string.IsNullOrEmpty(m_options.Logfile))
            {
                var path = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(m_options.Logfile));
                if (!System.IO.Directory.Exists(path))
                    System.IO.Directory.CreateDirectory(path);

                m_logTarget.AddTarget(
                    new Library.Logging.StreamLogDestination(m_options.Logfile),
                    m_options.LogFileLoglevel,
                    m_options.LogFileLogFilter
                );
            }



            if (m_options.HasTempDir)
            {
                Library.Utility.TempFolder.SystemTempPath = m_options.TempDir;
            }

            if (m_options.HasForcedLocale)
            {
                try
                {
                    var locale = m_options.ForcedLocale;
                    DoGetLocale(out m_resetLocale, out m_resetLocaleUI);
                    m_doResetLocale = true;
                    // Wrap the call to avoid loading issues for the setLocale method
                    DoSetLocale(locale, locale);
                }
                catch (Exception ex) // or only: MissingMethodException
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LocaleChangeError", ex, Strings.Controller.FailedForceLocaleError(ex.Message));
                    m_doResetLocale = false;
                    m_resetLocale = m_resetLocaleUI = null;
                }
            }

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
            {
                m_resetPriority = System.Threading.Thread.CurrentThread.Priority;
                System.Threading.Thread.CurrentThread.Priority = Library.Utility.Utility.ParsePriority(m_options.ThreadPriority);
            }

            if (string.IsNullOrEmpty(m_options.Dbpath))
                m_options.Dbpath = DatabaseLocator.GetDatabasePath(m_backend, m_options);

            ValidateOptions();
        }

        /// <summary>
        /// This function will examine all options passed on the commandline, and test for unsupported or deprecated values.
        /// Any errors will be logged into the statistics module.
        /// </summary>
        private void ValidateOptions()
        {
            // Check if only one of the retention options is set
            var selectedRetentionOptions = new List<String>();

            if (m_options.KeepTime.Ticks > 0)
            {
                selectedRetentionOptions.Add("keep-time");
            }

            if (m_options.KeepVersions > 0)
            {
                selectedRetentionOptions.Add("keep-versions");
            }

            if (m_options.RetentionPolicy.Any())
            {
                selectedRetentionOptions.Add("retention-policy");
            }

            if (selectedRetentionOptions.Count() > 1)
            {
                throw new Interface.UserInformationException(string.Format("Setting multiple retention options ({0}) is not permitted",
                    String.Join(", ", selectedRetentionOptions.Select(x => "--" + x))), "MultipleRetentionOptionsNotSupported");
            }

            // Check Prefix
            if (!string.IsNullOrWhiteSpace(m_options.Prefix) && m_options.Prefix.Contains("-"))
                throw new Interface.UserInformationException("The prefix cannot contain hyphens (-)", "PrefixCannotContainHyphens");

            //Check validity of retention-policy option value
            try
            {
                foreach (var configEntry in m_options.RetentionPolicy)
                {
                    if (!configEntry.IsKeepAllVersions() && !configEntry.IsUnlimtedTimeframe() &&
                        configEntry.Interval >= configEntry.Timeframe)
                    {
                        throw new Interface.UserInformationException("An interval cannot be bigger than the timeframe it is in", "IntervalCannotBeBiggerThanTimeFrame");
                    }
                }
            }
            catch (Exception e) // simply reading the option value might also result in an exception due to incorrect formatting
            {
                throw new Interface.UserInformationException(string.Format("An error occoured while processing the value of --{0}", "retention-policy"), "RetentionPolicyParseError", e);
            }

            //Keep a list of all supplied options
            Dictionary<string, string> ropts = new Dictionary<string, string>(m_options.RawOptions);

            //Keep a list of all supported options
            Dictionary<string, Library.Interface.ICommandLineArgument> supportedOptions = new Dictionary<string, Library.Interface.ICommandLineArgument>();

            //There are a few internal options that are not accessible from outside, and thus not listed
            foreach (string s in Options.InternalOptions)
                supportedOptions[s] = null;

            //Figure out what module options are supported in the current setup
            List<Library.Interface.ICommandLineArgument> moduleOptions = new List<Duplicati.Library.Interface.ICommandLineArgument>();
            Dictionary<string, string> disabledModuleOptions = new Dictionary<string, string>();

            foreach (KeyValuePair<bool, Library.Interface.IGenericModule> m in m_options.LoadedModules)
                if (m.Value.SupportedCommands != null)
                    if (m.Key)
                        moduleOptions.AddRange(m.Value.SupportedCommands);
                    else
                        foreach (Library.Interface.ICommandLineArgument c in m.Value.SupportedCommands)
                        {
                            disabledModuleOptions[c.Name] = m.Value.DisplayName + " (" + m.Value.Key + ")";

                            if (c.Aliases != null)
                                foreach (string s in c.Aliases)
                                    disabledModuleOptions[s] = disabledModuleOptions[c.Name];
                        }

            // Throw url-encoded options into the mix
            //TODO: This can hide values if both commandline and url-parameters supply the same key
            var ext = new Library.Utility.Uri(m_backend).QueryParameters;
            foreach(var k in ext.AllKeys)
                ropts[k] = ext[k];

            //Now run through all supported options, and look for deprecated options
            foreach (IList<Library.Interface.ICommandLineArgument> l in new IList<Library.Interface.ICommandLineArgument>[] {
                m_options.SupportedCommands,
                DynamicLoader.BackendLoader.GetSupportedCommands(m_backend),
                m_options.NoEncryption ? null : DynamicLoader.EncryptionLoader.GetSupportedCommands(m_options.EncryptionModule),
                moduleOptions,
                DynamicLoader.CompressionLoader.GetSupportedCommands(m_options.CompressionModule) })
            {
                if (l != null)
                    foreach (Library.Interface.ICommandLineArgument a in l)
                    {
                        if (supportedOptions.ContainsKey(a.Name) && !Options.KnownDuplicates.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
                            Logging.Log.WriteWarningMessage(LOGTAG, "DuplicateOption", null, Strings.Controller.DuplicateOptionNameWarning(a.Name));

                        supportedOptions[a.Name] = a;

                        if (a.Aliases != null)
                            foreach (string s in a.Aliases)
                            {
                                if (supportedOptions.ContainsKey(s) && !Options.KnownDuplicates.Contains(s, StringComparer.OrdinalIgnoreCase))
                                    Logging.Log.WriteWarningMessage(LOGTAG, "DuplicateOption", null, Strings.Controller.DuplicateOptionNameWarning(s));

                                supportedOptions[s] = a;
                            }

                        if (a.Deprecated)
                        {
                            List<string> aliases = new List<string>();
                            aliases.Add(a.Name);
                            if (a.Aliases != null)
                                aliases.AddRange(a.Aliases);

                            foreach (string s in aliases)
                                if (ropts.ContainsKey(s))
                                {
                                    string optname = a.Name;
                                    if (a.Name != s)
                                        optname += " (" + s + ")";

                                    Logging.Log.WriteWarningMessage(LOGTAG, "DeprecatedOption", null, Strings.Controller.DeprecatedOptionUsedWarning(optname, a.DeprecationMessage), null);
                                }

                        }
                    }
            }

            //Now look for options that were supplied but not supported
            foreach (string s in ropts.Keys)
                if (!supportedOptions.ContainsKey(s))
                    if (disabledModuleOptions.ContainsKey(s))
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedDisabledModule", null, Strings.Controller.UnsupportedOptionDisabledModuleWarning(s, disabledModuleOptions[s]), null);
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedOption", null, Strings.Controller.UnsupportedOptionWarning(s), null);

            //Look at the value supplied for each argument and see if is valid according to its type
            foreach (string s in ropts.Keys)
            {
                Library.Interface.ICommandLineArgument arg;
                if (supportedOptions.TryGetValue(s, out arg) && arg != null)
                {
                    string validationMessage = ValidateOptionValue(arg, s, ropts[s]);
                    if (validationMessage != null)
                        Logging.Log.WriteWarningMessage(LOGTAG, "OptionValidationError", null, validationMessage);
                }
            }

            // For now, warn not to use 7z
            if (string.Equals(m_options.CompressionModule, "7z", StringComparison.OrdinalIgnoreCase))
                Logging.Log.WriteWarningMessage(LOGTAG, "7zModuleHasIssues", null, "The 7z compression module has known issues and should only be used for experimental purposes");

            // Amazon CD is closing August 16th 2019
			if (string.Equals(new Library.Utility.Uri(m_backend).Scheme, "amzcd", StringComparison.OrdinalIgnoreCase))
				Logging.Log.WriteWarningMessage(LOGTAG, "AmzCDClosingApi", null, "The Amazon Cloud Drive API is closing down on August 16th 2019, please migrate your backups before this date");

			//TODO: Based on the action, see if all options are relevant
		}

		/// <summary>
		/// Helper method that expands the users chosen source input paths,
		/// and removes duplicate paths
		/// </summary>
		/// <returns>The expanded and filtered sources.</returns>
		private string[] ExpandInputSources(string[] inputsources, IFilter filter)
        {
            if (inputsources == null || inputsources.Length == 0)
                throw new Duplicati.Library.Interface.UserInformationException(Strings.Controller.NoSourceFoldersError, "NoSourceFolders");

            var sources = new List<string>(inputsources.Length);

            System.IO.DriveInfo[] drives = null;

            //Make sure they all have the same format and exist
            foreach (var inputsource in inputsources)
            {
                List<string> expandedSources = new List<string>();

                if (Platform.IsClientWindows && (inputsource.StartsWith("*:", StringComparison.Ordinal) || inputsource.StartsWith("?:", StringComparison.Ordinal)))
                {
                    // *: drive paths are only supported on Windows clients
                    // Lazily load the drive info
                    drives = drives ?? System.IO.DriveInfo.GetDrives();

                    // Replace the drive letter with each available drive
                    string sourcePath = inputsource.Substring(1);
                    foreach (System.IO.DriveInfo drive in drives)
                    {
                        string expandedSource = drive.Name[0] + sourcePath;
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AddingSourcePathFromWildcard", @"Adding source path ""{0}"" due to wildcard source path ""{1}""", expandedSource, inputsource);
                        expandedSources.Add(expandedSource);
                    }
                }
                else if (Platform.IsClientWindows && inputsource.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                {
                    // In order to specify a drive by it's volume name, adopt the volume guid path syntax:
                    //   \\?\Volume{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
                    // The volume guid can be found using the 'mountvol' commandline tool.
                    // However, instead of using this path with Windows APIs directory, it is adapted here to a standard path.
                    Guid volumeGuid;
                    if (Guid.TryParse(inputsource.Substring(@"\\?\Volume{".Length, @"XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX".Length), out volumeGuid))
                    {
                        string driveLetter = Library.Utility.Utility.GetDriveLetterFromVolumeGuid(volumeGuid);
                        if (!string.IsNullOrEmpty(driveLetter))
                        {
                            string expandedSource = driveLetter + inputsource.Substring(@"\\?\Volume{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}".Length);
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AddingSourceFromGuid", @"Adding source path ""{0}"" in place of volume guid source path ""{1}""", expandedSource, inputsource);
                            expandedSources.Add(expandedSource);
                        }
                        else
                        {
                            // If we aren't allow to have missing sources, throw an exception indicating we couldn't find a drive where this volume is mounted
                            if (!m_options.AllowMissingSource)
                                throw new Duplicati.Library.Interface.UserInformationException(Strings.Controller.SourceVolumeNameNotFoundError(inputsource, volumeGuid), "MissingSourceFolder");
                        }
                    }
                    else
                    {
                        // If we aren't allow to have missing sources, throw an exception indicating we couldn't find this volume
                        if (!m_options.AllowMissingSource)
                            throw new Duplicati.Library.Interface.UserInformationException(Strings.Controller.SourceVolumeNameInvalidError(inputsource), "SourceVolumeNameInvalid");
                    }
                }
                else
                {
                    expandedSources.Add(inputsource);
                }

                bool foundAnyPaths = false;
                bool unauthorized = false;
                foreach (string expandedSource in expandedSources)
                {
                    string source;
                    try
                    {
                        // TODO: This expands "C:" to CWD, but not C:\
                        source = System.IO.Path.GetFullPath(expandedSource);
                    }
                    catch (Exception ex)
                    {
                        // Note that we use the original source (with the *) in the error
                        throw new Duplicati.Library.Interface.UserInformationException(Strings.Controller.InvalidPathError(expandedSource, ex.Message), "InputSourceInvalid", ex);
                    }

                    var fi = new System.IO.FileInfo(source);
                    var di = new System.IO.DirectoryInfo(source);
                    if (fi.Exists || di.Exists)
                    {
                        foundAnyPaths = true;

                        if (!fi.Exists)
                            source = Util.AppendDirSeparator(source);

                        sources.Add(source);
                    }
                    else
                    {
                        try
                        {
                            // Try to get attributes. Returns -1 if source doesn't exist, otherwise throws an exception.
                            // In this case, it is irrelevant to use fileinfo or directoryinfo to retrieve attributes.
                            var unused = fi.Attributes;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "AddingSourceFolder",
                                                            ex, @"Insufficient permissions to read ""{0}"", skipping", expandedSource);
                            unauthorized = true;
                        }
                    }
                }

                // If no paths were found, and we aren't allowed to have missing sources, throw an error
                if (!foundAnyPaths && !m_options.AllowMissingSource)
                {
                    if (unauthorized)
                    {
                        throw new System.IO.IOException(Strings.Controller.SourceUnauthorizedError(inputsource));
                    }

                    throw new System.IO.IOException(Strings.Controller.SourceIsMissingError(inputsource));
                }
            }

            //Sanity check for duplicate files/folders
            ISet<string> pathDuplicates;
            sources = Library.Utility.Utility.GetUniqueItems(sources, Library.Utility.Utility.ClientFilenameStringComparer, out pathDuplicates).OrderBy(a => a).ToList();

            foreach (var pathDuplicate in pathDuplicates)
                Logging.Log.WriteVerboseMessage(LOGTAG, "RemoveDuplicateSource", "Removing duplicate source: {0}", pathDuplicate);

            //Sanity check for multiple inclusions of the same folder
            for (int i = 0; i < sources.Count; i++)
                for (int j = 0; j < sources.Count; j++)
                    if (i != j && sources[i].StartsWith(sources[j], Library.Utility.Utility.ClientFilenameStringComparison) && sources[i].EndsWith(Util.DirectorySeparatorString, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        if (filter != null)
                        {
                            bool excludes;

                            FilterExpression.AnalyzeFilters(filter, out _, out excludes);

                            // If there are no excludes, there is no need to keep the folder as a filter
                            if (excludes)
                            {
                                Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a subfolder of \"{1}\", and using it as an include filter", sources[i], sources[j]);
                                filter = Library.Utility.JoinedFilterExpression.Join(new FilterExpression(sources[i]), filter);
                            }
                            else
                                Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a subfolder or subfile of \"{1}\"", sources[i], sources[j]);
                        }
                        else
                            Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a subfolder or subfile of \"{1}\"", sources[i], sources[j]);

                        sources.RemoveAt(i);
                        i--;
                        break;
                    }

            return sources.ToArray();
        }

        /// <summary>
        /// Checks if the value passed to an option is actually valid.
        /// </summary>
        /// <param name="arg">The argument being validated</param>
        /// <param name="optionname">The name of the option to validate</param>
        /// <param name="value">The value to check</param>
        /// <returns>Null if no errors are found, an error message otherwise</returns>
        private static string ValidateOptionValue(Library.Interface.ICommandLineArgument arg, string optionname, string value)
        {
            if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration)
            {
                bool found = false;
                foreach (string v in arg.ValidValues ?? new string[0])
                    if (string.Equals(v, value, StringComparison.CurrentCultureIgnoreCase))
                    {
                        found = true;
                        break;
                    }

                if (!found)
                    return Strings.Controller.UnsupportedEnumerationValue(optionname, value, arg.ValidValues ?? new string[0]);

            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Flags)
            {
                bool validatedAllFlags = false;
                var flags = (value ?? string.Empty).ToLowerInvariant().Split(new[] {","}, StringSplitOptions.None).Select(flag => flag.Trim()).Distinct();
                var validFlags = arg.ValidValues ?? new string[0];

                foreach (var flag in flags)
                {
                    if (!validFlags.Any(validFlag => string.Equals(validFlag, flag, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        validatedAllFlags = false;
                        break;
                    }

                    validatedAllFlags = true;
                }

                if (!validatedAllFlags)
                {
                    return Strings.Controller.UnsupportedFlagsValue(optionname, value, validFlags);
                }
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean)
            {
                if (!string.IsNullOrEmpty(value) && Library.Utility.Utility.ParseBool(value, true) != Library.Utility.Utility.ParseBool(value, false))
                    return Strings.Controller.UnsupportedBooleanValue(optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Integer)
            {
                if (!long.TryParse(value, out _))
                    return Strings.Controller.UnsupportedIntegerValue(optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path)
            {
                foreach (string p in value.Split(System.IO.Path.DirectorySeparatorChar))
                    if (p.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                        return Strings.Controller.UnsupportedPathValue(optionname, p);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Size)
            {
                try
                {
                    Library.Utility.Sizeparser.ParseSize(value);
                }
                catch
                {
                    return Strings.Controller.UnsupportedSizeValue(optionname, value);
                }

                if (!string.IsNullOrWhiteSpace(value) && char.IsDigit(value.Last()))
                    return Strings.Controller.NonQualifiedSizeValue(optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan)
            {
                try
                {
                    Library.Utility.Timeparser.ParseTimeSpan(value);
                }
                catch
                {
                    return Strings.Controller.UnsupportedTimeValue(optionname, value);
                }
            }

            return null;
        }

        public void Pause()
        {
            var ct = m_currentTask;
            if (ct != null)
                ct.Pause();
        }

        public void Resume()
        {
            var ct = m_currentTask;
            if (ct != null)
                ct.Resume();
        }

        public void Stop()
        {
            var ct = m_currentTask;
            if (ct != null)
                ct.Stop();
        }

        public void Abort()
        {
            var ct = m_currentTask;
            if (ct != null)
                ct.Abort();

            var t = m_currentTaskThread;
            if (t != null)
                t.Abort();
        }

        public long MaxUploadSpeed
        {
            get { return m_options.MaxUploadPrSecond; }
            set { m_options.MaxUploadPrSecond = value; }
        }

        public long MaxDownloadSpeed
        {
            get { return m_options.MaxDownloadPrSecond; }
            set { m_options.MaxDownloadPrSecond = value; }
        }

        /// <summary>
        /// Time of last compact operation
        /// </summary>
        public DateTime LastCompact { get; set; }

        /// <summary>
        /// Time of last vacuum operation
        /// </summary>
        public DateTime LastVacuum { get; set; }

        private void CheckAutoCompactInterval()
        {
            if (!m_options.NoAutoCompact && (LastCompact > DateTime.MinValue) && (LastCompact.Add(m_options.AutoCompactInterval) > DateTime.Now))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "CompactResults", "Skipping auto compaction until {0}", LastCompact.Add(m_options.AutoCompactInterval));
                m_options.RawOptions["no-auto-compact"] = "true";
            }
        }

        private void CheckAutoVacuumInterval()
        {
            if (m_options.AutoVacuum && (LastVacuum > DateTime.MinValue) && (LastVacuum.Add(m_options.AutoVacuumInterval) > DateTime.Now))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "VacuumResults", "Skipping auto vacuum until {0}", LastVacuum.Add(m_options.AutoVacuumInterval));
                m_options.RawOptions["auto-vacuum"] = "false";
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion
    }
}
