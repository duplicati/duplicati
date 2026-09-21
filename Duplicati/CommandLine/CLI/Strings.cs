// Copyright (C) 2026, The Duplicati Team
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

using Duplicati.Library.Localization.Short;
namespace Duplicati.CommandLine.Strings
{
    internal static class Program
    {
        public static string DeleteCommandNeedsOptions(string commandname, string[] options) { return LC.L(@"The command {0} needs at least one of the following options set: {1}", commandname, string.Join(", ", options)); }
        public static string WrongNumberOfCommandsError_v2(int actualcommands, int expectedcommands, string[] commands) { return LC.L(@"Found {0} commands but expected {1}, commands: 
{2}", actualcommands, expectedcommands, string.Join(System.Environment.NewLine, commands ?? new string[0])); }
        public static string InvalidCommandError(string commandname) { return LC.L(@"Command not supported: {0}", commandname); }
        public static string NoFilesetsMatching { get { return LC.L(@"No filesets matched the criteria."); } }
        public static string WouldDeleteBackups { get { return LC.L(@"The following filesets would be deleted:"); } }
        public static string DeletedBackups { get { return LC.L(@"These filesets were deleted:"); } }
        public static string SupportedBackendsHeader { get { return LC.L(@"Supported backends:"); } }
        public static string SupportedCompressionModulesHeader { get { return LC.L(@"Supported compression modules:"); } }
        public static string SupportedEncryptionModulesHeader { get { return LC.L(@"Supported encryption modules:"); } }
        public static string SupportedParityModulesHeader { get { return LC.L(@"Supported parity modules:"); } }
        public static string SupportedOptionsHeader { get { return LC.L(@"Supported options:"); } }
        public static string ModuleIsLoadedAutomatically { get { return LC.L(@"Module is loaded automatically. Use --{0} to prevent this.", "disable-module"); } }
        public static string ModuleIsNotLoadedAutomatically { get { return LC.L(@"Module is not loaded automatically Use --{0} to load it.", "enable-module"); } }
        public static string GenericModulesHeader { get { return LC.L(@"Supported generic modules:"); } }
        public static string FailedToParseParametersFileError(string path, string message) { return LC.L(@"Unable to read the parameters file ""{0}"", reason: {1}", path, message); }
        public static string FiltersCannotBeUsedWithFileError2 { get { return LC.L(@"Filters cannot be specified on the commandline if filters are also present in the parameter file. Use the special --{0}, --{1}, or --{2} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {3}.", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
        public static string InternalOptionUsedError(string optionname) { return LC.L(@"The option --{0} was supplied, but it is reserved for internal use and may not be set on the commandline.", optionname); }
        public static string ParametersFileOptionLong2 { get { return LC.L(@"Use this option to store some or all of the options given to the commandline client. The file must be a plain text file, and UTF-8 encoding is preferred. Each line in the file should be of the format --option=value. Use the special options --{0} and --{1} to override the localpath and the remote destination uri, respectively. The options in this file take precedence over the options provided on the commandline. You cannot specify filters in both the file and on the commandline. Instead, you can use the special --{2}, --{3}, or --{4} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {5}.", "source", "target", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
        public static string ParametersFileOptionShort { get { return LC.L(@"Path to a file with parameters"); } }
        public static string UnhandledException(string message) { return LC.L(@"An error occured: {0}", message); }
        public static string UnhandledInnerException(string message) { return LC.L(@"The inner error message is: {0}", message); }
        public static string IncludeLong { get { return LC.L(@"Include files that match this filter. The special character * means any number of character, and the special character ? means any single character. Use *.txt to include all files with a txt extension. Regular expressions are also supported and can be supplied by using hard braces, e.g. [.*\.txt]. Filter groups (which encapsulate a built-in set of well-known files and folders) can be specified by using curly braces, e.g. {{Applications}}."); } }
        public static string IncludeShort { get { return LC.L(@"Include files"); } }
        public static string ExcludeLong { get { return LC.L(@"Exclude files that match this filter. The special character * means any number of character, and the special character ? means any single character. Use *.txt to exclude all files with a txt extension. Regular expressions are also supported and can be supplied by using hard braces, e.g. [.*\.txt]. Filter groups (which encapsulate a built-in set of well-known files and folders) can be specified by using curly braces, e.g. {{TemporaryFiles}}."); } }
        public static string ExcludeShort { get { return LC.L(@"Exclude files"); } }
        public static string ControlFilesOptionLong { get { return LC.L(@"If this option is used with a backup operation, it is interpreted as a list of files to add to the filesets. When used with list or restore, it will list or restore the control files instead of the normal files."); } }
        public static string ControlFilesOptionShort { get { return LC.L(@"Use control files"); } }
        public static string QuietConsoleOptionLong { get { return LC.L(@"If this option is set, progress reports and other messages that would normally go to the console will be redirected to the log."); } }
        public static string QuietConsoleOptionShort { get { return LC.L(@"Disable console output"); } }
        public static string SkippingSourceArgumentsOnNonBackupOperation { get { return @"The --source argument was specified in the parameter file, but the current operation is not a backup operation, so the argument is ignored."; } }
        public static string SkippingTargetArgumentOnTestFilters { get { return @"The --target argument was specified in the parameter file, but the test-filters operation has no target, so the argument is ignored."; } }
        public static string AutoUpdateOptionShort { get { return LC.L(@"Toggle automatic updates"); } }
        public static string AutoUpdateOptionLong { get { return LC.L(@"Set this option if you prefer to have the commandline version automatically update"); } }
        public static string PortableModeOptionShort { get { return LC.L(@"Use portable mode"); } }
        public static string PortableModeOptionLong { get { return LC.L(@"If this option is set, the configuration files will be stored in a data subfolder of the Duplicati installation folder. This is useful for running from a USB stick or other portable media."); } }
        public static string DataFolderOptionShort { get { return LC.L(@"Data folder"); } }
        public static string DataFolderOptionLong { get { return LC.L(@"The folder where data is stored. This is the folder where the database and other files are stored."); } }
        public static string AllowedBackendModulesShort { get { return LC.L(@"Allowed backends"); } }
        public static string AllowedBackendModulesLong { get { return LC.L(@"Comma-separated list of backend protocol keys that are allowed to be used. If this option is not specified, all available backends are allowed. Use this option to restrict the backends that can be used, for example in a managed environment. Example: --allowed-backend-modules=file,ftp,s3"); } }
        public static string AllowedEncryptionModulesShort { get { return LC.L(@"Allowed encryption modules"); } }
        public static string AllowedEncryptionModulesLong { get { return LC.L(@"Comma-separated list of encryption module file extensions that are allowed to be used. If this option is not specified, all available encryption modules are allowed. Use this option to restrict the encryption modules that can be used, for example in a managed environment. Example: --allowed-encryption-modules=aes,gpg"); } }
        public static string AllowedCompressionModulesShort { get { return LC.L(@"Allowed compression modules"); } }
        public static string AllowedCompressionModulesLong { get { return LC.L(@"Comma-separated list of compression module file extensions that are allowed to be used. If this option is not specified, all available compression modules are allowed. Use this option to restrict the compression modules that can be used, for example in a managed environment. Example: --allowed-compression-modules=zip"); } }
        public static string RestoreTestStarted(System.DateTime time) { return LC.L(@"Restore test started at {0}", time); }
        public static string BackupRestoreTestStarting { get { return LC.L(@"Testing restore of a sample from the backup ..."); } }
        public static string BackupRestoreTestSummary(long tested, long passed, long failed, long skipped) { return LC.L(@"  Restore test: {0} file(s) tested, {1} passed, {2} failed, {3} skipped", tested, passed, failed, skipped); }
        public static string RestoreTestSummary(string mode, long version, int seed) { return LC.L(@"Restore test completed for version {0} in {1} mode (seed {2})", version, mode, seed); }
        public static string RestoreTestFilesSummary(long tested, long passed, long failed, long skipped) { return LC.L(@"  Files tested: {0}, passed: {1}, failed: {2}, skipped: {3}", tested, passed, failed, skipped); }
        public static string RestoreTestDataSummary(string restored, string downloaded, long volumes) { return LC.L(@"  Data verified: {0}, downloaded: {1} in {2} remote volume(s)", restored, downloaded, volumes); }
        public static string RestoreTestDatabaseRecreated { get { return LC.L(@"  Database: recreated from the remote destination"); } }
        public static string RestoreTestDatabaseReused { get { return LC.L(@"  Database: existing local database used"); } }
        public static string RestoreTestDuration(System.TimeSpan duration) { return LC.L(@"  Duration: {0:hh\:mm\:ss}", duration); }
        public static string RestoreTestBudgetExceeded(string reason) { return LC.L(@"  Budget exceeded: {0}", reason); }
        public static string RestoreTestFailuresHeader(long count) { return LC.L(@"  {0} file(s) failed verification:", count); }
        public static string RestoreTestFailureLine(string path, string reason, string expected, string actual) { return LC.L(@"    {0}: {1} (expected: {2}, actual: {3})", path, reason, expected, actual); }
        public static string RestoreTestSourceDifferencesHeader(long count) { return LC.L(@"  {0} file(s) differ from the source:", count); }
        public static string RestoreTestSourceDifferenceLine(string path, string reason) { return LC.L(@"    {0}: {1}", path, reason); }
        public static string RestoreTestAndMore(long count, string optionname) { return LC.L(@"    ... and {0} more (use --{1} to see all)", count, optionname); }
        public static string RestoreTestNoFilesTested { get { return LC.L(@"No files were tested, is the backup empty or excluded by the filters?"); } }
        public static string RestoreTestAllPassed(long count) { return LC.L(@"All {0} tested file(s) were restored and verified successfully", count); }

        // ReSharper disable once UnusedMember.Global
        // This is a placeholder message that is intended to be used with the code
        // for each error and log message. The idea is that the commandline will
        // use the code to provide a link to the forum, searching for the code,
        // such that it is easy to maintain a list of help links.
        public static string YouMayGetMoreHelpHere(string url) { return LC.L(@"This link may provide additional information: {0}", url); }
    }
}
