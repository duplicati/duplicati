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
using System.Collections.Generic;
using Duplicati.Library.Localization.Short;
using System.Linq;

namespace Duplicati.Server.Strings
{
        public static class Program
        {
                public static string AnotherInstanceDetected { get { return LC.L(@"Another instance is running, and was notified"); } }
                public static string DatabaseOpenError(string message) { return LC.L(@"Failed to create, open or upgrade the database.
Error message: {0}", message); }
                public static string HelpCommandDescription { get { return LC.L(@"Display this help"); } }
                public static string HelpDisplayDialog { get { return LC.L(@"Supported commandline arguments:

"); } }
                public static string HelpDisplayFormat(string optionname, string optiontext) { return LC.L(@"--{0}: {1}", optionname, optiontext); }
                public static string ParametersFileOptionLong2 { get { return LC.L(@"Use this option to store some or all of the options given to the commandline client. The file must be a plain text file, and UTF-8 encoding is preferred. Each line in the file should be of the format --option=value. Use the special options --{0} and --{1} to override the localpath and the remote destination uri, respectively. The options in this file take precedence over the options provided on the commandline. You cannot specify filters in both the file and on the commandline. Instead, you can use the special --{2}, --{3}, or --{4} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {5} ", "source", "target", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
                public static string ParametersFileOptionShort { get { return LC.L(@"Path to a file with parameters"); } }
                public static string FiltersCannotBeUsedWithFileError2 { get { return LC.L(@"Filters cannot be specified on the commandline if filters are also present in the parameter file. Use the special --{0}, --{1}, or --{2} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {3}", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
                public static string FailedToParseParametersFileError(string path, string message) { return LC.L(@"Unable to read the parameters file ""{0}"", reason: {1}", path, message); }
                public static string SkippingSourceArgumentsOnNonBackupOperation { get { return @"The --source argument was specified in the parameter file, but the current operation is not a backup operation, so the argument is ignored"; } }
                public static string LogfileCommandDescription { get { return LC.L(@"Output log information to the file given"); } }
                public static string LoglevelCommandDescription { get { return LC.L(@"Determine the amount of information written in the log file"); } }
                public static string LogConsoleDescription { get { return LC.L(@"Output log information to the console"); } }
                public static string PortablemodeCommandDescription { get { return LC.L(@"Activate portable mode where the database is placed below the program executable"); } }
                public static string SeriousError(string message) { return LC.L(@"A serious error occurred in Duplicati: {0}", message); }
                public static string TearDownError(string message) { return LC.L(@"An error occurred on server tear down: {0}", message); }
                public static string StartupFailure(System.Exception error) { return LC.L(@"Unable to start up. Perhaps another process is already running?
Error message: {0}", error); }
                public static string UnencrypteddatabaseCommandDescription { get { return LC.L(@"Disable database encryption"); } }
                public static string WrongSQLiteVersion(System.Version actualversion, string expectedversion) { return LC.L(@"Unsupported version of SQLite detected ({0}), must be {1} or higher", actualversion, expectedversion); }
                public static string WebserverWebrootDescription { get { return LC.L(@"The path to the folder where the static files for the webserver is present. The folder must be located beneath the installation folder."); } }
                public static string WebserverPortDescription { get { return LC.L(@"The port the webserver listens on. Multiple values may be supplied with a comma in between."); } }
                public static string WebserverDisableHTTPSDescription { get { return LC.L(@"Deactivates the use of HTTPS even if a certificate is stored in the database or provided on the commandline."); } }
                public static string WebserverRemoveCertificateDescription { get { return LC.L(@"Removes any existing certificate from the database. This option also disables HTTPS."); } }
                public static string WebserverCertificateFileDescription { get { return LC.L(@"The certificate and key file in PKCS #12 format the webserver use for SSL."); } }
                public static string WebserverCertificatePasswordDescription { get { return LC.L(@"The password for decryption of the provided certificate PKCS #12 file."); } }
                public static string WebserverInterfaceDescription { get { return LC.L(@"The interface the webserver listens on. The special values ""*"" and ""any"" means any interface. The special value ""loopback"" means the loopback adapter."); } }
                public static string WebserverPasswordDescription { get { return LC.L(@"The password required to access the webserver. This option is saved so you do not need to set it on each run. Setting an empty value disables the password."); } }
                public static string WebserverAllowedhostnamesDescription { get { return LC.L(@"The hostnames that are accepted, separated with semicolons. If any of the hostnames are ""*"", all hostnames are allowed and the hostname checking is disabled."); } }
                public static string PingpongkeepaliveLong { get { return LC.L(@"When running as a server, the service daemon must verify that the process is responding. If this option is enabled, the server reads stdin and writes a reply to each line read."); } }
                public static string PingpongkeepaliveShort { get { return LC.L(@"Enable the ping-pong responder"); } }
                public static string DisableupdatecheckShort { get { return LC.L(@"Disable the automatic update check"); } }
                public static string DisableupdatecheckLong { get { return LC.L(@"Use this option to disable the automatic update check. Manual update checks can still be performed."); } }
                public static string LogretentionLong { get { return LC.L(@"Set the time after which log data will be purged from the database."); } }
                public static string LogretentionShort { get { return LC.L(@"Clean up old log data"); } }
                public static string ServerdatafolderLong(string envname) { return LC.L(@"Duplicati needs to store a small database with all settings. Use this option to choose where the settings are stored. This option can also be set with the environment variable {0}.", envname); }
                public static string ServerdatafolderShort { get { return LC.L(@"Set the folder where settings are stored"); } }
                public static string ServerencryptionkeyLong(string envname, string decryptionoption) { return LC.L(@"This option sets the encryption key used to scramble the local settings database. This option can also be set with the environment variable {0}. Use the option --{1} to disable the database scrambling.", envname, decryptionoption); }
                public static string ServerencryptionkeyShort { get { return LC.L(@"Set the database encryption key"); } }
                public static string TempdirLong { get { return LC.L(@"Use this option to supply an alternative folder for temporary storage. By default the system default temporary folder is used. Note that also SQLite will put temporary files in this temporary folder."); } }
                public static string TempdirShort { get { return LC.L(@"Temporary storage folder"); } }
                public static string WebserverResetJwtConfigDescription { get { return LC.L(@"Reset the JWT configuration, invalidating any issued login tokens"); } }
                public static string WebserverEnableForeverTokenDescription { get { return LC.L(@"Enable the use of long-lived access tokens"); } }
                public static string WebserverDisableVisualCaptchaDescription { get { return LC.L(@"Disable the visual captcha"); } }
                public static string WebserverApiOnlyDescription { get { return LC.L(@"Disable the web interface and only allow API access"); } }
                public static string WebserverDisableSigninTokensDescription { get { return LC.L(@"Disable the use of signin tokens"); } }
                public static string WebserverSpaPathsDescription { get { return LC.L(@"The relative paths that should be served as single page applications, separated with semicolons."); } }
                public static string WebserverCorsOriginsDescription { get { return LC.L(@"A list of CORS origins to allow, separated with semicolons. Each origin must be a valid URL."); } }
                public static string WebserverTimezoneDescription { get { return LC.L(@"The timezone to use for the webserver. The timezone must be a valid timezone identifier, such as ""America/New_York"" or ""UTC"". Common three-letter abbreviations like ""CET"" are supported, but ambiguous in some cases."); } }
                public static string DisabledbencryptionLong { get { return LC.L(@"Use this option to disable database encryption of sensitive fields"); } }
                public static string DisabledbencryptionShort { get { return LC.L(@"Disable database encryption"); } }
                public static string LogwindowseventlogLong { get { return LC.L(@"Use this option to log to the Windows event log. The provided name is in the format Log:Source. If no log name is provided, Duplicati is used."); } }
                public static string LogwindowseventlogShort { get { return LC.L(@"Log to the Windows event log"); } }
                public static string LogwindowseventloglevelLong { get { return LC.L(@"Use this option to set the log level for the Windows event log."); } }
                public static string LogwindowseventloglevelShort { get { return LC.L(@"Set the log level for the Windows event log"); } }
                public static string WindowsEventLogSourceNotFound(string source) { return LC.L(@"The Windows event log source {0} was not found. Attempting to create.", source); }
                public static string WindowsEventLogSourceNotCreated(string source) { return LC.L(@"The Windows Event Log was not created for: {0}, not logging to eventlog.", source); }
                public static string WindowsEventLogNotSupported { get { return LC.L(@"The Windows event log is not supported on this platform"); } }
                public static string ServerStarted(string @interface, int port) { return LC.L(@"Server has started and is listening on {0}, port {1}", @interface, port); }
                public static string ServerStartedSignin(string url) { return LC.L(@"Use the following link to sign in: {0}", url); }
                public static string ServerCrashed(string message) { return LC.L(@"The server crashed: {0}", message); }
                public static string ServerStopping { get { return LC.L(@"Server is stopping, tearing down handlers"); } }
                public static string ServerStopped { get { return LC.L(@"Server has stopped"); } }
                public static string RequiredbencryptionLong { get { return LC.L(@"Use this option to require a custom provided key for database encryption of sensitive fields and not rely on the serial number."); } }
                public static string RequiredbencryptionShort { get { return LC.L(@"Require database encryption"); } }
                public static string DatabaseEncryptionKeyRequired(string envkey, string disableoptionname) { return LC.L(@"Database encryption key is required. Supply an encryption key via the environment variable {0} or disable database encryption with the option --{1}", envkey, disableoptionname); }
                public static string BlacklistedEncryptionKey(string envkey, string disableoptionname) { return LC.L(@"The database encryption key is blacklisted and cannot be used. The database has been decrypted. Supply a new encryption key via the environment variable {0} or disable database encryption with the option --{1}", envkey, disableoptionname); }
                public static string NoEncryptionKeySpecified(string envkey, string disableoptionname) { return LC.L(@"No database encryption key was found. The database will be stored unencrypted. Supply an encryption key via the environment variable {0} or disable database encryption with the option --{1}", envkey, disableoptionname); }
                public static string EncryptionKeyMissing(string envkey) { return LC.L(@"The database appears to be encrypted, but no key was specified. Opening the database will likely fail. Use the environment variable {0} to specify the key.", envkey); }
                public static string InvalidTimezone(string timezone) { return LC.L(@"The timezone {0} is not valid", timezone); }
                public static string SettingsencryptionkeyShort { get { return LC.L(@"Set the encryption key for the settings database"); } }
                public static string SettingsencryptionkeyLong(string envname) { return LC.L(@"Use this option to set the encryption key for the settings database. This option can also be set with the environment variable {0}.", envname); }
                public static string InvalidPauseResumeState(LiveControls.LiveControlState state) { return LC.L(@"Invalid pause/resume state: {0}", state); }
        }
        internal static class Scheduler
        {
                public static string InvalidTimeSetupError(System.DateTime startdate, string interval, string alloweddays) { return LC.L(@"Unable to find a valid date, given the start date {0}, the repetition interval {1} and the allowed days {2}", startdate, interval, alloweddays); }
        }
        public static class Server
        {
                public static string StartedServer(string ip, int port) { return LC.L(@"Server has started and is listening on {0}, port {1}", ip, port); }
                public static string SSLCertificateFileMissingOption { get { return LC.L(@"SSL certificate password option has no meaning when provided without SSL certificate file option!"); } }
                public static string ServerStartFailure(IEnumerable<int> portstried) { return LC.L(@"Unable to open a socket for listening, tried ports: {0}", string.Join(",", from n in (portstried ?? new int[0]) select n.ToString())); }
        }

}
