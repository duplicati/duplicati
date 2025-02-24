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
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using System.IO;

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
        private string m_backendUrl;
        /// <summary>
        /// The parsed type-safe version of the commandline options
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The optional secret provider, if none provided in options
        /// </summary>
        public ISecretProvider SecretProvider { get; private set; }
        /// <summary>
        /// The destination for all output messages during execution
        /// </summary>
        private IMessageSink m_messageSink;

        /// <summary>
        /// The current executing task
        /// </summary>
        private ITaskControl m_currentTaskControl = null;

        /// <summary>
        /// If not null, active locale change that needs to be reset
        /// </summary>
        private LocaleChange m_localeChange = null;

        /// <summary>
        /// The multi-controller log target
        /// </summary>
        private ControllerMultiLogTarget m_logTarget;

        /// <summary>
        /// Callback method invoked when an operation is started
        /// </summary>
        public Action<IBasicResults> OnOperationStarted { get; set; }

        /// <summary>
        /// Callback method invoked when an operation is completed
        /// </summary>
        public Action<IBasicResults, Exception> OnOperationCompleted { get; set; }

        /// <summary>
        /// Constructs a new interface for performing backup and restore operations
        /// </summary>
        /// <param name="backendUrl">The url for the backend to use</param>
        /// <param name="options">All required options</param>
        public Controller(string backendUrl, Dictionary<string, string> options, IMessageSink messageSink)
        {
            m_backendUrl = backendUrl;
            m_options = new Options(options);
            m_messageSink = messageSink;
        }

        /// <summary>
        /// Appends another message sink to the controller
        /// </summary>
        /// <param name="sink">The sink to use.</param>
        public void AppendSink(IMessageSink sink)
        {
            if (this.m_messageSink is MultiMessageSink messageSink)
                messageSink.Append(sink);
            else
                m_messageSink = new MultiMessageSink(m_messageSink, sink);
        }

        /// <summary>
        /// Sets a secret provider to use for all operations
        /// </summary>
        /// <param name="secretProvider">The secret provider to use</param>
        public void SetSecretProvider(ISecretProvider secretProvider)
        {
            SecretProvider = secretProvider;
        }

        public Duplicati.Library.Interface.IBackupResults Backup(string[] inputsources, IFilter filter = null)
        {
            Library.UsageReporter.Reporter.Report("USE_BACKEND", new Library.Utility.Uri(m_backendUrl).Scheme);
            Library.UsageReporter.Reporter.Report("USE_COMPRESSION", m_options.CompressionModule);
            Library.UsageReporter.Reporter.Report("USE_ENCRYPTION", m_options.EncryptionModule);

            CheckAutoCompactInterval();
            CheckAutoVacuumInterval();

            return RunAction(new BackupResults(), ref inputsources, ref filter, (result, backendManager) =>
            {

                using (var h = new Operation.BackupHandler(m_backendUrl, m_options, result))
                {
                    h.RunAsync(ExpandInputSources(inputsources, filter), backendManager, filter).Await();
                }

                Library.UsageReporter.Reporter.Report("BACKUP_FILECOUNT", result.ExaminedFiles);
                Library.UsageReporter.Reporter.Report("BACKUP_FILESIZE", result.SizeOfExaminedFiles);
                Library.UsageReporter.Reporter.Report("BACKUP_DURATION", (long)result.Duration.TotalSeconds);
            });
        }

        public Library.Interface.IRestoreResults Restore(string[] paths, Library.Utility.IFilter filter = null)
        {
            return RunAction(new RestoreResults(), ref paths, ref filter, (result, backendManager) =>
            {
                new Operation.RestoreHandler(m_options, result).Run(paths, backendManager, filter);

                Library.UsageReporter.Reporter.Report("RESTORE_FILECOUNT", result.RestoredFiles);
                Library.UsageReporter.Reporter.Report("RESTORE_FILESIZE", result.SizeOfRestoredFiles);
                Library.UsageReporter.Reporter.Report("RESTORE_DURATION", (long)result.Duration.TotalSeconds);
            });
        }

        public Duplicati.Library.Interface.IRestoreControlFilesResults RestoreControlFiles(IEnumerable<string> files = null, Library.Utility.IFilter filter = null)
        {
            return RunAction(new RestoreControlFilesResults(), ref filter, (result, backendManager) =>
            {
                new Operation.RestoreControlFilesHandler(m_options, result).Run(files, backendManager, filter);
            });
        }

        public Duplicati.Library.Interface.IDeleteResults Delete()
        {
            return RunAction(new DeleteResults(), (result, backendManager) =>
            {
                new Operation.DeleteHandler(m_options, result).Run(backendManager);
            });
        }

        public Duplicati.Library.Interface.IRepairResults Repair(Library.Utility.IFilter filter = null)
        {
            return RunAction(new RepairResults(), ref filter, (result, backendManager) =>
            {
                new Operation.RepairHandler(m_options, result).Run(backendManager, filter);
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
            return RunAction(new ListResults(), ref filter, (result, backendManager) =>
            {
                new Operation.ListFilesHandler(m_options, result).Run(backendManager, filterstrings, filter).Await();
            });
        }

        public Duplicati.Library.Interface.IListResults ListControlFiles(IEnumerable<string> filterstrings, Library.Utility.IFilter filter)
        {
            return RunAction(new ListResults(), ref filter, (result, backendManager) =>
            {
                new Operation.ListControlFilesHandler(m_options, result).Run(backendManager, filterstrings, filter);
            });
        }

        public Duplicati.Library.Interface.IListRemoteResults ListRemote()
        {
            return RunAction(new ListRemoteResults(), (result, backendManager) =>
            {
                using (var tf = System.IO.File.Exists(m_options.Dbpath) ? null : new Library.Utility.TempFile())
                using (var db = new Database.LocalDatabase(((string)tf) ?? m_options.Dbpath, "list-remote", true))
                    result.SetResult(backendManager.ListAsync(CancellationToken.None).Await());
            });
        }

        public Duplicati.Library.Interface.IListRemoteResults DeleteAllRemoteFiles()
        {
            return RunAction(new ListRemoteResults(), (result, backendManager) =>
            {
                var cancelToken = CancellationToken.None;
                result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Listing);
                {
                    // Only delete files that match the expected pattern and prefix
                    var list = backendManager.ListAsync(cancelToken).Await()
                        .Select(x => Volumes.VolumeBase.ParseFilename(x))
                        .Where(x => x != null)
                        .Where(x => x.Prefix == m_options.Prefix)
                        .ToList();

                    // If the local database is available, we will use it to avoid deleting unrelated files
                    // from the backend. Otherwise, we may accidentally delete non-Duplicati files, or
                    // files from a different Duplicati configuration that points to the same backend location
                    // and uses the same prefix (see issues #2678, #3845, and #4244).
                    if (System.IO.File.Exists(m_options.Dbpath))
                    {
                        using (LocalDatabase db = new LocalDatabase(m_options.Dbpath, "list-remote", true))
                        {
                            IEnumerable<RemoteVolumeEntry> dbRemoteVolumes = db.GetRemoteVolumes();
                            HashSet<string> dbRemoteFiles = new HashSet<string>(dbRemoteVolumes.Select(x => x.Name));
                            list = list.Where(x => dbRemoteFiles.Contains(x.File.Name)).ToList();
                        }
                    }

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Deleting);
                    result.OperationProgressUpdater.UpdateProgress(0);
                    for (var i = 0; i < list.Count; i++)
                    {
                        try
                        {
                            backendManager.DeleteAsync(list[i].File.Name, list[i].File.Size, true, cancelToken).Await();
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

            return RunAction(new CompactResults(), (result, backendManager) =>
            {
                new Operation.CompactHandler(m_options, result).Run(backendManager).Await();
            });
        }

        public Duplicati.Library.Interface.IRecreateDatabaseResults UpdateDatabaseWithVersions(Library.Utility.IFilter filter = null)
        {
            var filelistfilter = Operation.RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version, singleTimeMatch: true);

            return RunAction(new RecreateDatabaseResults(), ref filter, (result, backendManager) =>
            {
                using (var h = new Operation.RecreateDatabaseHandler(m_options, result))
                    h.RunUpdate(backendManager, filter, filelistfilter, null);
            });
        }

        public Duplicati.Library.Interface.ICreateLogDatabaseResults CreateLogDatabase(string targetpath)
        {
            var t = new string[] { targetpath };

            return RunAction(new CreateLogDatabaseResults(), ref t, (result, backendManager) =>
            {
                new Operation.CreateBugReportHandler(t[0], m_options, result).Run();
            });
        }

        public Duplicati.Library.Interface.IListChangesResults ListChanges(string baseVersion, string targetVersion, IEnumerable<string> filterstrings = null, Library.Utility.IFilter filter = null, Action<Duplicati.Library.Interface.IListChangesResults, IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>>> callback = null)
        {
            var t = new string[] { baseVersion, targetVersion };

            return RunAction(new ListChangesResults(), ref t, ref filter, (result, backendManager) =>
            {
                new Operation.ListChangesHandler(m_options, result).Run(t[0], t[1], backendManager, filterstrings, filter, callback);
            });
        }

        public Duplicati.Library.Interface.IListAffectedResults ListAffected(List<string> args, Action<Duplicati.Library.Interface.IListAffectedResults> callback = null)
        {
            return RunAction(new ListAffectedResults(), (result, backendManager) =>
            {
                new Operation.ListAffected(m_options, result).Run(args, callback);
            });
        }

        public Duplicati.Library.Interface.ITestResults Test(long samples = 1)
        {
            if (!m_options.RawOptions.ContainsKey("full-remote-verification"))
                m_options.RawOptions["full-remote-verification"] = "true";

            return RunAction(new TestResults(), (result, backendManager) =>
            {
                new Operation.TestHandler(m_options, result).Run(samples, backendManager);
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
                return RunAction(new TestFilterResults(), ref paths, ref filter, (result, backendManager) =>
                {
                    new Operation.TestFilterHandler(m_options, result).RunAsync(ExpandInputSources(paths, filter), filter).Await();
                });
            }
        }

        public Library.Interface.ISystemInfoResults SystemInfo()
        {
            return RunAction(new SystemInfoResults(), (result, backendManager) =>
            {
                Operation.SystemInfoHandler.Run(result);
            });
        }

        public Library.Interface.IPurgeFilesResults PurgeFiles(Library.Utility.IFilter filter)
        {
            return RunAction(new PurgeFilesResults(), (result, backendManager) =>
            {
                new Operation.PurgeFilesHandler(m_options, result).Run(backendManager, filter);
            });
        }

        public Library.Interface.IListBrokenFilesResults ListBrokenFiles(Library.Utility.IFilter filter, Func<long, DateTime, long, string, long, bool> callbackhandler = null)
        {
            return RunAction(new ListBrokenFilesResults(), (result, backendManager) =>
            {
                new Operation.ListBrokenFilesHandler(m_options, result).Run(backendManager, filter, callbackhandler);
            });
        }

        public Library.Interface.IPurgeBrokenFilesResults PurgeBrokenFiles(Library.Utility.IFilter filter)
        {
            return RunAction(new PurgeBrokenFilesResults(), (result, backendManager) =>
            {
                new Operation.PurgeBrokenFilesHandler(m_options, result).Run(backendManager, filter);
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
                return RunAction(new SendMailResults(), (result, backendManager) =>
                {
                    result.Lines = new string[0];
                    System.Threading.Thread.Sleep(5);
                });
            }
        }

        public Library.Interface.IVacuumResults Vacuum()
        {
            return RunAction(new VacuumResults(), (result, backendManager) =>
            {
                new Operation.VacuumHandler(m_options, result).Run();
            });
        }

        private T RunAction<T>(T result, Action<T, IBackendManager> method)
            where T : ISetCommonOptions, ITaskControlProvider, Logging.ILogDestination, IBasicResults, IBackendWriterProvider
        {
            var tmp = new string[0];
            IFilter tempfilter = null;
            return RunAction<T>(result, ref tmp, ref tempfilter, method);
        }

        private T RunAction<T>(T result, ref string[] paths, Action<T, IBackendManager> method)
            where T : ISetCommonOptions, ITaskControlProvider, Logging.ILogDestination, IBasicResults, IBackendWriterProvider
        {
            IFilter tempfilter = null;
            return RunAction<T>(result, ref paths, ref tempfilter, method);
        }

        private T RunAction<T>(T result, ref IFilter filter, Action<T, IBackendManager> method)
            where T : ISetCommonOptions, ITaskControlProvider, Logging.ILogDestination, IBasicResults, IBackendWriterProvider
        {
            var tmp = new string[0];
            return RunAction<T>(result, ref tmp, ref filter, method);
        }

        private T RunAction<T>(T result, ref string[] paths, ref IFilter filter, Action<T, IBackendManager> method)
            where T : ISetCommonOptions, ITaskControlProvider, Logging.ILogDestination, IBasicResults, IBackendWriterProvider
        {
            OnOperationStarted?.Invoke(result);
            var resultSetter = result as ISetCommonOptions;
            m_logTarget = new ControllerMultiLogTarget(result, Logging.LogMessageType.Information, null);
            using (Logging.Log.StartScope(m_logTarget, null))
            {
                m_logTarget.AddTarget(m_messageSink, m_options.ConsoleLoglevel, m_options.ConsoleLogFilter);
                result.MessageSink = m_messageSink;

                try
                {
                    m_currentTaskControl = result.TaskControl;
                    m_options.MainAction = result.MainOperation;
                    ApplySecretProvider(CancellationToken.None).Await();
                    SetupCommonOptions(result, ref paths, ref filter);
                    Logging.Log.WriteInformationMessage(LOGTAG, "StartingOperation", Strings.Controller.StartingOperationMessage(m_options.MainAction));

                    using (new ProcessController(m_options))
                    using (new Logging.Timer(LOGTAG, string.Format("Run{0}", result.MainOperation), string.Format("Running {0}", result.MainOperation)))
                    using (new CoCoL.IsolatedChannelScope())
                    using (m_options.ConcurrencyMaxThreads <= 0 ? null : new CoCoL.CappedThreadedThreadPool(m_options.ConcurrencyMaxThreads))
                    using (var backend = new Backend.BackendManager(m_backendUrl, m_options, result.BackendWriter, result.TaskControl))
                    {
                        method(result, backend);

                        // TODO: Should also have a single shared database connection for all operations
                        // The transactions should be managed inside the connection, and not passed around

                        // This would allow us to pass the database instance to the backend manager
                        // And safeguard against remote operations not being logged in the database
                        if (File.Exists(m_options.Dbpath))
                        {
                            using (var db = new LocalDatabase(m_options.Dbpath, result.MainOperation.ToString(), true))
                                backend.StopRunnerAndFlushMessages(db, null).Await();
                        }
                        else
                        {
                            backend.StopRunnerAndDiscardMessages();
                        }
                    }

                    if (resultSetter.EndTime.Ticks == 0)
                        resultSetter.EndTime = DateTime.UtcNow;
                    result.SetDatabase(null);
                    if (result is BasicResults r)
                    {
                        r.Interrupted = false;
                    }

                    OperationComplete(result, null);

                    Logging.Log.WriteInformationMessage(LOGTAG, "CompletedOperation", Strings.Controller.CompletedOperationMessage(m_options.MainAction));

                    return result;
                }
                catch (Exception ex)
                {
                    resultSetter.EndTime = DateTime.UtcNow;

                    if (ex is Library.Interface.OperationAbortException oae)
                    {
                        // Log this as a normal operation, as the script raising the exception,
                        // has already populated either warning or log messages as required
                        Logging.Log.WriteInformationMessage(LOGTAG, "AbortOperation", "Aborting operation by request, requested result: {0}", oae.AbortReason);

                        if (result is BasicResults basicResults)
                        {
                            basicResults.Interrupted = true;
                            try
                            {
                                // No operation was started in database, so write logs to new operation
                                using (var db = new LocalDatabase(m_options.Dbpath, result.MainOperation.ToString(), true))
                                {
                                    basicResults.SetDatabase(db);
                                    db.WriteResults();
                                }

                                // Do not propagate the cancel exception
                                OperationComplete(result, null);
                            }
                            catch { }
                        }
                        else
                        {
                            // Perform the module shutdown
                            OperationComplete(result, ex);
                        }

                        return result;
                    }
                    else
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FailedOperation", ex, Strings.Controller.FailedOperationMessage(m_options.MainAction, ex.Message));

                        if (result is BasicResults basicResults)
                        {
                            try
                            {
                                basicResults.OperationProgressUpdater.UpdatePhase(OperationPhase.Error);
                                basicResults.Fatal = true;
                                // Write logs to previous operation if database exists
                                if (LocalDatabase.Exists(m_options.Dbpath))
                                {
                                    using (var db = new LocalDatabase(m_options.Dbpath, null, true))
                                    {
                                        basicResults.SetDatabase(db);
                                        db.WriteResults();
                                    }
                                }

                                // Report the result, and the failure
                                OperationComplete(result, ex);

                            }
                            catch { }
                        }
                        else
                        {
                            // Perform the module shutdown
                            OperationComplete(result, ex);
                        }

                        throw;
                    }

                }
                finally
                {
                    m_currentTaskControl = null;
                }
            }
        }

        private void OperationComplete(IBasicResults result, Exception exception)
        {
            if (m_options != null && m_options.LoadedModules != null)
            {
                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is IGenericCallbackModule module)
                        try { module.OnFinish(result, exception); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"OnFinishError{mx.Key}", ex, "OnFinish callback {0} failed: {1}", mx.Key, ex.Message); }

                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is IDisposable disposable)
                        try { disposable.Dispose(); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"DisposeError{mx.Key}", ex, "Dispose for {0} failed: {1}", mx.Key, ex.Message); }

                m_options.LoadedModules.Clear();
            }

            if (m_localeChange != null)
            {
                m_localeChange.Dispose();
                m_localeChange = null;
            }

            if (m_logTarget != null)
            {
                m_logTarget.Dispose();
                m_logTarget = null;
            }

            OnOperationCompleted?.Invoke(result, exception);
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
            var qp = new Library.Utility.Uri(m_backendUrl).QueryParameters;
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

                    if (mx.Value is IGenericSourceModule sourcemodule)
                    {
                        if (sourcemodule.ContainFilesForBackup(paths))
                        {
                            var sourceoptions = sourcemodule.ParseSourcePaths(ref paths, ref pristinefilter, m_options.RawOptions);

                            foreach (var sourceoption in sourceoptions)
                                m_options.RawOptions[sourceoption.Key] = sourceoption.Value;
                        }
                    }

                    if (mx.Value is IGenericCallbackModule module)
                        module.OnStart(result.MainOperation.ToString(), ref m_backendUrl, ref paths);
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
                    m_localeChange = new LocaleChange(m_options.ForcedLocale);
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LocaleChangeError", ex, Strings.Controller.FailedForceLocaleError(ex.Message));
                }
            }

            if (string.IsNullOrEmpty(m_options.Dbpath))
                m_options.Dbpath = CLIDatabaseLocator.GetDatabasePathForCLI(m_backendUrl, m_options);

            ValidateOptions();
        }

        private async Task ApplySecretProvider(CancellationToken cancellationToken)
        {
            var args = new[] { new Library.Utility.Uri(m_backendUrl) };
            await SecretProviderHelper.ApplySecretProviderAsync([], args, m_options.RawOptions, TempFolder.SystemTempPath, SecretProvider, cancellationToken);
            // Write back the backend argument, if it was modified by the secret provider
            m_backendUrl = args[0].ToString();
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
            var ropts = new Dictionary<string, string>(m_options.RawOptions);

            //Keep a list of all supported options
            var supportedOptions = new Dictionary<string, Library.Interface.ICommandLineArgument>();

            //There are a few internal options that are not accessible from outside, and thus not listed
            foreach (string s in Options.InternalOptions)
                supportedOptions[s] = null;

            //Figure out what module options are supported in the current setup
            var moduleOptions = new List<Duplicati.Library.Interface.ICommandLineArgument>();
            var disabledModuleOptions = new Dictionary<string, string>();

            foreach (var m in m_options.LoadedModules)
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
            var ext = new Library.Utility.Uri(m_backendUrl).QueryParameters;
            foreach (var k in ext.AllKeys)
                ropts[k] = ext[k];

            //Now run through all supported options, and look for deprecated options
            foreach (var l in new IEnumerable<ICommandLineArgument>[] {
                m_options.SupportedCommands,
                DynamicLoader.BackendLoader.GetSupportedCommands(m_backendUrl),
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
            foreach (var s in ropts.Keys)
                if (!supportedOptions.ContainsKey(s))
                    if (disabledModuleOptions.ContainsKey(s))
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedDisabledModule", null, Strings.Controller.UnsupportedOptionDisabledModuleWarning(s, disabledModuleOptions[s]), null);
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedOption", null, Strings.Controller.UnsupportedOptionWarning(s), null);

            //Look at the value supplied for each argument and see if is valid according to its type
            foreach (var s in ropts.Keys)
            {
                if (supportedOptions.TryGetValue(s, out var arg) && arg != null)
                {
                    string validationMessage = ValidateOptionValue(arg, s, ropts[s]);
                    if (validationMessage != null)
                        Logging.Log.WriteWarningMessage(LOGTAG, "OptionValidationError", null, validationMessage);
                }
            }

            //Inform the user about the deprecated Tardigrade-Backend. They should switch to Storj DCS instead.
            if (string.Equals(new Library.Utility.Uri(m_backendUrl).Scheme, "tardigrade", StringComparison.OrdinalIgnoreCase))
                Logging.Log.WriteWarningMessage(LOGTAG, "TardigradeRename", null, "The Tardigrade-backend got renamed to Storj DCS - please migrate your backups to the new configuration by changing the destination storage type to Storj DCS.");

            //Inform the user about the unmaintained Mega support library
            if (string.Equals(new Library.Utility.Uri(m_backendUrl).Scheme, "mega", StringComparison.OrdinalIgnoreCase))
                Logging.Log.WriteWarningMessage(LOGTAG, "MegaUnmaintained", null, "The Mega support library is currently unmaintained and may not work as expected. Mega has not published an official API so it may break at any moment. Please consider migrating to another backend.");

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

                if (OperatingSystem.IsWindows() && (inputsource.StartsWith("*:", StringComparison.Ordinal) || inputsource.StartsWith("?:", StringComparison.Ordinal)))
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
                else if (OperatingSystem.IsWindows() && inputsource.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
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
                        // Check if this is a mounted path
                        if (expandedSource.StartsWith("@"))
                        {
                            // TODO: If the remote source fails to load,
                            // this will be an enumeration warning, but will result
                            // in the backup being recorded without files from the source
                            // Eventually, this could lead to retention deletion,
                            // causing the last backup with the data from the source to be deleted
                            foundAnyPaths = true;
                            sources.Add(expandedSource);
                            continue;
                        }

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
            sources = Library.Utility.Utility.GetUniqueItems(sources, Library.Utility.Utility.ClientFilenameStringComparer, out pathDuplicates).ToList();

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
                    if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
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
                var flags = (value ?? string.Empty).ToLowerInvariant().Split(new[] { "," }, StringSplitOptions.None).Select(flag => flag.Trim()).Distinct();
                var validFlags = arg.ValidValues ?? new string[0];

                foreach (var flag in flags)
                {
                    if (!validFlags.Any(validFlag => string.Equals(validFlag, flag, StringComparison.OrdinalIgnoreCase)))
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

        public void Pause(bool alsoTransfers)
        {
            var ct = m_currentTaskControl;
            if (ct != null)
                ct.Pause(alsoTransfers);
        }

        public void Resume()
        {
            var ct = m_currentTaskControl;
            if (ct != null)
                ct.Resume();
        }

        public void Stop()
        {
            var ct = m_currentTaskControl;
            if (ct == null)
                return;

            Logging.Log.WriteVerboseMessage(LOGTAG, "CancellationRequested", "Cancellation Requested");
            ct.Stop();
        }

        public void Abort()
        {
            m_currentTaskControl?.Terminate();
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
