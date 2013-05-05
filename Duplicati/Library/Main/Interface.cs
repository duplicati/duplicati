#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// The operations that Duplicati can report
    /// </summary>
    public enum DuplicatiOperation
    {
        /// <summary>
        /// Indicates that the operations is a full or incremental backup
        /// </summary>
        Backup,
        /// <summary>
        /// Indicates that the operation is a restore type operation
        /// </summary>
        Restore,
        /// <summary>
        /// Indicates that the operation is a list type operation
        /// </summary>
        List,
        /// <summary>
        /// Indicates that the operation is a delete type operation
        /// </summary>
        Remove
    }

    /// <summary>
    /// The actual operation that Duplicati supports
    /// </summary>
    public enum DuplicatiOperationMode
    {
        /// <summary>
        /// A backup operation, either full or incremental
        /// </summary>
        Backup,
        /// <summary>
        /// A full backup
        /// </summary>
        BackupFull,
        /// <summary>
        /// An incremental backup
        /// </summary>
        BackupIncremental,
        /// <summary>
        /// A restore operation
        /// </summary>
        Restore,
        /// <summary>
        /// A restore operation for control files
        /// </summary>
        RestoreControlfiles,
        /// <summary>
        /// A backend file listing
        /// </summary>
        List,
        /// <summary>
        /// A list of backup chains found on the backend
        /// </summary>
        GetBackupSets,
        /// <summary>
        /// A list of files found in a specific backup set, produced by summing through incremental signature files
        /// </summary>
        ListCurrentFiles,
        /// <summary>
        /// A list of the source folders found in a specific backup set
        /// </summary>
        ListSourceFolders,
        /// <summary>
        /// A list of files found in a specific backup set, only shows content from the single backup set and not the entire chain
        /// </summary>
        ListActualSignatureFiles,
        /// <summary>
        /// A delete operation performed by looking at the number of existing full backups
        /// </summary>
        DeleteAllButNFull,
        /// <summary>
        /// A delete operation performed by looking at the number of existing backups
        /// </summary>
        DeleteAllButN,
        /// <summary>
        /// A delete operation performed by looking at the age of existing backups
        /// </summary>
        DeleteOlderThan,
        /// <summary>
        /// A cleanup operation that removes orphan files
        /// </summary>
        CleanUp,
        /// <summary>
        /// A request to create the underlying folder
        /// </summary>
        CreateFolder,
        /// <summary>
        /// A search for files in signature files
        /// </summary>
        FindLastFileVersion,
        /// <summary>
        /// Verifies the hashes and backup chain
        /// </summary>
        Verify,
        /// <summary>
        /// Creates a log of the database suitable for attaching to a bug report
        /// </summary>
        CreateLogDb
    }

    /// <summary>
    /// An enum that describes the level of verification done by the verify command
    /// </summary>
    public enum VerificationLevel
    {
        /// <summary>
        /// Just verify the manifest chain
        /// </summary>
        Manifest,
        /// <summary>
        /// Verify the manifest chain and all signature files
        /// </summary>
        Signature,
        /// <summary>
        /// Verify everything, including the content files, requires download of all files
        /// </summary>
        Full,
    }

    /// <summary>
    /// A delegate for reporting progress from within the Duplicati module
    /// </summary>
    /// <param name="caller">The instance that is running</param>
    /// <param name="operation">The overall operation type</param>
    /// <param name="specificoperation">A more specific type of operation</param>
    /// <param name="progress">The current overall progress of the operation</param>
    /// <param name="subprogress">The progress of a transfer</param>
    /// <param name="message">A message describing the current operation</param>
    /// <param name="submessage">A message describing the current transfer operation</param>
    public delegate void OperationProgressEvent(Interface caller, DuplicatiOperation operation, DuplicatiOperationMode specificoperation, int progress, int subprogress, string message, string submessage);

    /// <summary>
    /// A delegate used to report metadata about the current backup, obtained while running a normal operation
    /// </summary>
    /// <param name="metadata">A table with various metadata informations</param>
    public delegate void MetadataReportDelegate(IDictionary<string, string> metadata);

    public class Interface : IDisposable
    {
        /// <summary>
        /// The amount of progressbar allocated for reading incremental data
        /// </summary>
        private const double INCREMENAL_COST = 0.10;
        /// <summary>
        /// The amount of progressbar allocated for uploading async volumes
        /// </summary>
        private const double ASYNC_RESERVED = 0.10;

        /// <summary>
        /// The backend url
        /// </summary>
        private string m_backend;
        /// <summary>
        /// The parsed type-safe version of the commandline options
        /// </summary>
        private Options m_options;

        /// <summary>
        /// The restult of an operation, used to report via IGenericCallbackModule
        /// </summary>
        private object m_result;

        /// <summary>
        /// The amount of progressbar allocated for reading incremental data
        /// </summary>
        private double m_incrementalFraction = INCREMENAL_COST;
        /// <summary>
        /// The amount of progressbar allocated for uploading the remaining volumes in asynchronous mode
        /// </summary>
        private double m_asyncReserved = 0.0;
        /// <summary>
        /// The current overall progress without taking the reserved amounts into account
        /// </summary>
        private double m_progress = 0.0;
        /// <summary>
        /// The number of restore patches
        /// </summary>
        private int m_restorePatches = 0;
        /// <summary>
        /// Cache of the last progress message
        /// </summary>
        private string m_lastProgressMessage = "";
        /// <summary>
        /// A flag indicating if logging has been set, used to dispose the logging
        /// </summary>
        private bool m_hasSetLogging = false;

        /// <summary>
        /// A flag toggling if the upload progress is reported, 
        /// used to prevent showing the progress bar when performing
        /// ansynchronous uploads
        /// </summary>
        private bool m_allowUploadProgress = true;
        /// <summary>
        /// When allowing the upload progress to be reported,
        /// there can be a flicker because the upload progresses,
        /// before the flag is disabled again. This value
        /// is set to DateTime.Now.AddSeconds(1) before
        /// entering a potentially blocking operation, which
        /// allows the progress to be reported if the call blocks,
        /// but prevents flicker from non-blocking calls.
        /// </summary>
        private DateTime m_allowUploadProgressAfter = DateTime.Now;

        public event OperationProgressEvent OperationStarted;
        public event OperationProgressEvent OperationCompleted;
        public event OperationProgressEvent OperationProgress;
        public event OperationProgressEvent OperationError;
        public event MetadataReportDelegate MetadataReport;

        /// <summary>
        /// This gets called whenever execution of an operation is started or stopped; it currently handles the AllowSleep option
        /// </summary>
        /// <param name="isRunning">Flag indicating execution state</param>
        private void OperationRunning(bool isRunning)
        {          
            if (m_options!=null && !m_options.AllowSleep && !Duplicati.Library.Utility.Utility.IsClientLinux)
                try
                {
                    Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS | (isRunning ? Win32.EXECUTION_STATE.ES_SYSTEM_REQUIRED : 0));
                }
                catch { } //TODO: Report this somehow
        }   

        /// <summary>
        /// Returns the current overall operation mode based on the actual operation mode
        /// </summary>
        /// <returns>The overall operation type</returns>
        private DuplicatiOperation GetOperationType()
        {
            switch (m_options.MainAction)
            {
                case DuplicatiOperationMode.Backup:
                case DuplicatiOperationMode.BackupFull:
                case DuplicatiOperationMode.BackupIncremental:
                    return DuplicatiOperation.Backup;
                case DuplicatiOperationMode.Restore:
                case DuplicatiOperationMode.RestoreControlfiles:
                    return DuplicatiOperation.Restore;
                case DuplicatiOperationMode.List:
                case DuplicatiOperationMode.GetBackupSets:
                case DuplicatiOperationMode.ListCurrentFiles:
                case DuplicatiOperationMode.ListSourceFolders:
                case DuplicatiOperationMode.ListActualSignatureFiles:
                case DuplicatiOperationMode.FindLastFileVersion:
                case DuplicatiOperationMode.Verify:
                    return DuplicatiOperation.List;
                case DuplicatiOperationMode.DeleteAllButNFull:
                case DuplicatiOperationMode.DeleteOlderThan:
                case DuplicatiOperationMode.CleanUp:
                    return DuplicatiOperation.Remove;
                
                default:
                    throw new Exception(string.Format(Strings.Interface.UnexpectedOperationTypeError, m_options.MainAction));
            }
        }

        /// <summary>
        /// Constructs a new interface for performing backup and restore operations
        /// </summary>
        /// <param name="backend">The url for the backend to use</param>
        /// <param name="options">All required options</param>
        public Interface(string backend, Dictionary<string, string> options)
        {
            m_backend = backend;
            m_options = new Options(options);
            OperationProgress += new OperationProgressEvent(Interface_OperationProgress);
        }

        /// <summary>
        /// Event handler for the OperationProgres, used to store the last status message
        /// </summary>
        private void Interface_OperationProgress(Interface caller, DuplicatiOperation operation, DuplicatiOperationMode specificoperation, int progress, int subprogress, string message, string submessage)
        {
            m_lastProgressMessage = message;
        }

        public string Backup(string[] sources)
        {
            BackupStatistics bs = new BackupStatistics(DuplicatiOperationMode.Backup);
            SetupCommonOptions(bs, ref sources);

            using (new Logging.Timer("Backup from " + string.Join(";", sources) + " to " + m_backend))
            {
                var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

                if (string.IsNullOrEmpty(fhopts.Fhdbpath))
                    throw new Exception(string.Format(Strings.Interface.MissingDatabasepathError, "fh-dbpath"));

                if (sources == null || sources.Length == 0)
                    throw new Exception(Strings.Interface.NoSourceFoldersError);

                //Make sure they all have the same format and exist
                for (int i = 0; i < sources.Length; i++)
                {
                    sources[i] = Utility.Utility.AppendDirSeparator(System.IO.Path.GetFullPath(sources[i]));

                    if (!System.IO.Directory.Exists(sources[i]))
                        throw new System.IO.IOException(String.Format(Strings.Interface.SourceFolderIsMissingError, sources[i]));
                }

                //Sanity check for duplicate folders and multiple inclusions of the same folder
                for (int i = 0; i < sources.Length - 1; i++)
                {
                    for (int j = i + 1; j < sources.Length; j++)
                        if (sources[i].Equals(sources[j], Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                            throw new Exception(string.Format(Strings.Interface.SourceDirIsIncludedMultipleTimesError, sources[i]));
                        else if (sources[i].StartsWith(sources[j], Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                            throw new Exception(string.Format(Strings.Interface.SourceDirsAreRelatedError, sources[i], sources[j]));
                }

                ForestHash.ForestHash.Backup(m_backend, fhopts, bs, sources);
                return bs.ToString();
            }
        }

        public string Restore(string[] target)
        {
            if (target == null || target.Length != 1)
                throw new Exception("Cannot specify more than a single target folder");

            var rs = new RestoreStatistics(DuplicatiOperationMode.Restore);
            SetupCommonOptions(rs, ref target);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            ForestHash.ForestHash.Restore(m_backend, fhopts, rs, target[0]);
            return rs.ToString();
        }

        public string RestoreControlFiles(string target)
        {
            var rs = new RestoreStatistics(DuplicatiOperationMode.RestoreControlfiles);
            SetupCommonOptions(rs);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            using (var handler = new ForestHash.Operation.RestoreControlFilesHandler(m_backend, fhopts, rs, target))
                handler.Run();

            return rs.ToString();
        }

        public string DeleteAllButN()
        {
            var rs = new RestoreStatistics(DuplicatiOperationMode.DeleteAllButN);
            SetupCommonOptions(rs);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            using (var handler = new ForestHash.Operation.DeleteHandler(m_backend, fhopts, rs))
                handler.Run();

            return rs.ToString();
        }

        public string Repair()
        {
            var rs = new RestoreStatistics(DuplicatiOperationMode.CleanUp);
            SetupCommonOptions(rs);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            using (var handler = new ForestHash.Operation.RepairHandler(m_backend, fhopts, rs))
                handler.Run();

            return rs.ToString();
        }

        public List<string> ListCurrentFiles()
        {
            var rs = new RestoreStatistics(DuplicatiOperationMode.ListCurrentFiles);
            SetupCommonOptions(rs);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            using (var handler = new ForestHash.Operation.ListFilesHandler(m_backend, fhopts, rs))
                return handler.Run();
        }

        private void SetupCommonOptions(CommunicationStatistics stats)
        {
            string[] tmp = null;
            SetupCommonOptions(stats, ref tmp);
        }

        private void SetupCommonOptions(CommunicationStatistics stats, ref string[] paths)
        {
            m_options.MainAction = stats.OperationMode;
            
            switch (m_options.MainAction)
            {
                case DuplicatiOperationMode.Backup:
                case DuplicatiOperationMode.BackupFull:
                case DuplicatiOperationMode.BackupIncremental:
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
                m_options.LoadedModules.Add(new KeyValuePair<bool, Library.Interface.IGenericModule>(Array.IndexOf<string>(m_options.DisableModules, m.Key.ToLower()) < 0 && (m.LoadAsDefault || Array.IndexOf<string>(m_options.EnableModules, m.Key.ToLower()) >= 0), m));

            foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                if (mx.Key)
                {
                    mx.Value.Configure(m_options.RawOptions);
                    if (mx.Value is Library.Interface.IGenericCallbackModule)
                        ((Library.Interface.IGenericCallbackModule)mx.Value).OnStart(stats.OperationMode.ToString(), ref m_backend, ref paths);
                }

            OperationRunning(true);

            Library.Logging.Log.LogLevel = m_options.Loglevel;
            if (!string.IsNullOrEmpty(m_options.Logfile))
            {
                m_hasSetLogging = true;
                var path = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(m_options.Logfile));
                if (!System.IO.Directory.Exists(path))
                    System.IO.Directory.CreateDirectory(path);
                Library.Logging.Log.CurrentLog = new Library.Logging.StreamLog(m_options.Logfile);
            }

            if (stats != null)
            {
                stats.VerboseErrors = m_options.DebugOutput;
                stats.VerboseRetryErrors = m_options.VerboseRetryErrors;
                stats.QuietConsole = m_options.QuietConsole;
            }

            if (m_options.HasTempDir)
                Utility.TempFolder.SystemTempPath = m_options.TempDir;

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
                System.Threading.Thread.CurrentThread.Priority = Utility.Utility.ParsePriority(m_options.ThreadPriority);

            ValidateOptions(stats);

            Library.Logging.Log.WriteMessage(string.Format(Strings.Interface.StartingOperationMessage, m_options.MainAction), Logging.LogMessageType.Information);
        }


        public List<KeyValuePair<string, DateTime>> FindLastFileVersion()
        {
            var rs = new RestoreStatistics(DuplicatiOperationMode.FindLastFileVersion);
            SetupCommonOptions(rs);

            var fhopts = new ForestHash.FhOptions(m_options.RawOptions);

            using (var handler = new ForestHash.Operation.FindLastFileVersionHandler(m_backend, fhopts, rs))
                return handler.Run();
        }

        /// <summary>
        /// This function will examine all options passed on the commandline, and test for unsupported or deprecated values.
        /// Any errors will be logged into the statistics module.
        /// </summary>
        /// <param name="options">The commandline options given</param>
        /// <param name="backend">The backend url</param>
        /// <param name="stats">The statistics into which warnings are written</param>
        private void ValidateOptions(CommunicationStatistics stats)
        {
            //No point in going through with this if we can't report
            if (stats == null)
                return;

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
                    {
                        foreach (Library.Interface.ICommandLineArgument c in m.Value.SupportedCommands)
                        {
                            disabledModuleOptions[c.Name] = m.Value.DisplayName + " (" + m.Value.Key + ")";

                            if (c.Aliases != null)
                                foreach (string s in c.Aliases)
                                    disabledModuleOptions[s] = disabledModuleOptions[c.Name];
                        }
                    }
            
            // Throw url-encoded options into the mix
            //TODO: This can hide values if both commandline and url-parameters supply the same key
            foreach(var k in DynamicLoader.BackendLoader.GetExtraCommands(m_backend))
                ropts[k.Key] = k.Value;

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
                        if (supportedOptions.ContainsKey(a.Name) && Array.IndexOf(Options.KnownDuplicates, a.Name.ToLower()) < 0)
                            stats.LogWarning(string.Format(Strings.Interface.DuplicateOptionNameWarning, a.Name), null);

                        supportedOptions[a.Name] = a;

                        if (a.Aliases != null)
                            foreach (string s in a.Aliases)
                            {
                                if (supportedOptions.ContainsKey(s) && Array.IndexOf(Options.KnownDuplicates, s.ToLower()) < 0)
                                    stats.LogWarning(string.Format(Strings.Interface.DuplicateOptionNameWarning, s), null);

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

                                    stats.LogWarning(string.Format(Strings.Interface.DeprecatedOptionUsedWarning, optname, a.DeprecationMessage), null);
                                }

                        }
                    }
            }

            //Now look for options that were supplied but not supported
            foreach (string s in ropts.Keys)
                if (!supportedOptions.ContainsKey(s))
                    if (disabledModuleOptions.ContainsKey(s))
                        stats.LogWarning(string.Format(Strings.Interface.UnsupportedOptionDisabledModuleWarning, s, disabledModuleOptions[s]), null);
                    else
                        stats.LogWarning(string.Format(Strings.Interface.UnsupportedOptionWarning, s), null);

            //Look at the value supplied for each argument and see if is valid according to its type
            foreach (string s in ropts.Keys)
            {
                Library.Interface.ICommandLineArgument arg;
                if (supportedOptions.TryGetValue(s, out arg) && arg != null)
                {
                    string validationMessage = ValidateOptionValue(arg, s, ropts[s]);
                    if (validationMessage != null)
                        stats.LogWarning(validationMessage, null);
                }
            }

            //TODO: Based on the action, see if all options are relevant
        }

        #region Static interface

        /// <summary>
        /// Checks if the value passed to an option is actually valid.
        /// </summary>
        /// <param name="arg">The argument being validated</param>
        /// <param name="optionname">The name of the option to validate</param>
        /// <param name="value">The value to check</param>
        /// <returns>Null if no errors are found, an error message otherwise</returns>
        public static string ValidateOptionValue(Library.Interface.ICommandLineArgument arg, string optionname, string value)
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
                    return string.Format(Strings.Interface.UnsupportedEnumerationValue, optionname, value, string.Join(", ", arg.ValidValues ?? new string[0]));

            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean)
            {
                if (!string.IsNullOrEmpty(value) && Utility.Utility.ParseBool(value, true) != Utility.Utility.ParseBool(value, false))
                    return string.Format(Strings.Interface.UnsupportedBooleanValue, optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Integer)
            {
                long l;
                if (!long.TryParse(value, out l))
                    return string.Format(Strings.Interface.UnsupportedIntegerValue, optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path)
            {
                foreach (string p in value.Split(System.IO.Path.DirectorySeparatorChar))
                    if (p.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                        return string.Format(Strings.Interface.UnsupportedPathValue, optionname, p);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Size)
            {
                try
                {
                    Utility.Sizeparser.ParseSize(value);
                }
                catch
                {
                    return string.Format(Strings.Interface.UnsupportedSizeValue, optionname, value);
                }
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan)
            {
                try
                {
                    Utility.Timeparser.ParseTimeSpan(value);
                }
                catch
                {
                    return string.Format(Strings.Interface.UnsupportedTimeValue, optionname, value);
                }
            }

            return null;
        }
            
        public static string Backup(string[] source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.Backup(source);
        }

        public static string Restore(string source, string[] target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.Restore(target);
        }

        public static IList<string> ListCurrentFiles(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListCurrentFiles();
        }
        
        public static string DeleteAllButN(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.DeleteAllButN();
        }

        public static string DeleteOlderThan(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.DeleteAllButN();
        }

        public static string Repair(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.Repair();
        }

        public static string RestoreControlFiles(string source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.RestoreControlFiles(target);
        }

        public static List<KeyValuePair<string, DateTime>> FindLastFileVersion(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.FindLastFileVersion();
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_options != null && m_options.LoadedModules != null)
            {
                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is Duplicati.Library.Interface.IGenericCallbackModule)
                        try { ((Duplicati.Library.Interface.IGenericCallbackModule)mx.Value).OnFinish(m_result); }
                        catch (Exception ex) { Logging.Log.WriteMessage(string.Format("OnFinish callback {0} failed: {1}", mx.Key, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex); }

                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is IDisposable)
                        try { ((IDisposable)mx.Value).Dispose(); }
                        catch (Exception ex) { Logging.Log.WriteMessage(string.Format("Dispose for {0} failed: {1}", mx.Key, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex); }

                m_options.LoadedModules.Clear();
                OperationRunning(false);
            }

            if (m_hasSetLogging && Logging.Log.CurrentLog is Logging.StreamLog)
            {
                Logging.StreamLog sl = (Logging.StreamLog)Logging.Log.CurrentLog;
                Logging.Log.CurrentLog = null;
                sl.Dispose();
                m_hasSetLogging = false;
            }
        }

        #endregion

        public static IEnumerable<ForestHash.Volumes.IParsedVolume> ParseFhFileList(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            stat = stat ?? new CommunicationStatistics(DuplicatiOperationMode.List);
            using(var i = new Interface(target, options))
            {
            	i.SetupCommonOptions(stat);
	            return ForestHash.ForestHash.ParseFileList(target, options, stat);
			}
        }

        public static string CompactBlocks(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            stat = stat ?? new CommunicationStatistics(DuplicatiOperationMode.CleanUp);
            using(var i = new Interface(target, options))
            {
            	i.SetupCommonOptions(stat);
            	return ForestHash.ForestHash.CompactBlocks(target, options, stat);
            }
        }
        
        public static string RecreateDatabase(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            stat = stat ?? new CommunicationStatistics(DuplicatiOperationMode.CleanUp);
            using(var i = new Interface(target, options))
            {
            	i.SetupCommonOptions(stat);
	            return ForestHash.ForestHash.RecreateDatabase(target, options, stat);
            }
        }

        public static string DeleteFilesets(string target, string filesets, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            stat = stat ?? new CommunicationStatistics(DuplicatiOperationMode.DeleteAllButN);
            using(var i = new Interface(target, options))
            {
            	i.SetupCommonOptions(stat);
	            return ForestHash.ForestHash.DeleteFilesets(target, filesets, options, stat);
            }
        }

        public static string CreateLogDatabase(string target, Dictionary<string, string> options, CommunicationStatistics stat)
        {
            stat = stat ?? new CommunicationStatistics(DuplicatiOperationMode.CreateLogDb);
            using(var i = new Interface(target, options))
            {
            	i.SetupCommonOptions(stat);
	            return ForestHash.ForestHash.CreateLogDatabase(target, options, stat);
            }
        }
    }
}
