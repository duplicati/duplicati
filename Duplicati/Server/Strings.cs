using System.Collections.Generic;
using Duplicati.Library.Localization.Short;
using System.Linq;

namespace Duplicati.Server.Strings {
    internal static class Program {
        public static string AnotherInstanceDetected { get { return LC.L(@"Another instance is running, and was notified"); } }
        public static string DatabaseOpenError(string message) { return LC.L(@"Failed to create, open or upgrade the database.
Error message: {0}", message); }
        public static string HelpCommandDescription { get { return LC.L(@"Displays this help"); } }
        public static string HelpDisplayDialog { get { return LC.L(@"Supported commandline arguments:

"); } }
        public static string HelpDisplayFormat(string optionname, string optiontext) { return LC.L(@"--{0}: {1}", optionname, optiontext); }
        public static string ParametersFileOptionLong2 { get { return LC.L(@"This option can be used to store some or all of the options given to the commandline client. The file must be a plain text file, UTF-8 encoding is preferred. Each line in the file should be of the format --option=value. The special options --{0} and --{1} can be used to override the localpath and the remote destination uri, respectively. The options in this file take precedence over the options provided on the commandline. You cannot specify filters in both the file and on the commandline. Instead, you can use the special --{2}, --{3}, or --{4} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {5} ", "source", "target", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
        public static string ParametersFileOptionShort { get { return LC.L(@"Path to a file with parameters"); } }
        public static string FiltersCannotBeUsedWithFileError2 { get { return LC.L(@"Filters cannot be specified on the commandline if filters are also present in the parameter file. Use the special --{0}, --{1}, or --{2} options to specify filters inside the parameter file. Each filter must be prefixed with either a + or a -, and multiple filters must be joined with {3}", "replace-filter", "append-filter", "prepend-filter", System.IO.Path.PathSeparator); } }
        public static string FailedToParseParametersFileError(string path, string message) { return LC.L(@"Unable to read the parameters file ""{0}"", reason: {1}", path, message); }
        public static string SkippingSourceArgumentsOnNonBackupOperation { get { return @"The --source argument was specified in the parameter file, but the current operation is not a backup operation, so the argument is ignored"; } }
        public static string LogfileCommandDescription { get { return LC.L(@"Outputs log information to the file given"); } }
        public static string LoglevelCommandDescription { get { return LC.L(@"Determines the amount of information written in the log file"); } }
        public static string PortablemodeCommandDescription { get { return LC.L(@"Activates portable mode where the database is placed below the program executable"); } }
        public static string SeriousError(string message) { return LC.L(@"A serious error occurred in Duplicati: {0}", message); }
        public static string StartupFailure(System.Exception error) { return LC.L(@"Unable to start up, perhaps another process is already running?
Error message: {0}", error); }
        public static string UnencrypteddatabaseCommandDescription { get { return LC.L(@"Disables database encryption"); } }
        public static string WrongSQLiteVersion(System.Version actualversion, string expectedversion) { return LC.L(@"Unsupported version of SQLite detected ({0}), must be {1} or higher", actualversion, expectedversion); }
        public static string WebserverWebrootDescription { get { return LC.L(@"The path to the folder where the static files for the webserver is present. The folder must be located beneath the installation folder"); } }
        public static string WebserverPortDescription { get { return LC.L(@"The port the webserver listens on. Multiple values may be supplied with a comma in between."); } }
        public static string WebserverCertificateFileDescription { get { return LC.L(@"The certificate and key file in PKCS #12 format the webserver use for SSL. Only RSA/DSA keys are supported."); } }
        public static string WebserverCertificatePasswordDescription { get { return LC.L(@"The password for decryption of certificate PKCS #12 file."); } }
        public static string WebserverInterfaceDescription { get { return LC.L(@"The interface the webserver listens on. The special values ""*"" and ""any"" means any interface. The special value ""loopback"" means the loopback adapter."); } }
        public static string WebserverPasswordDescription { get { return LC.L(@"The password required to access the webserver. This option is saved so you do not need to set it on each run. Setting an empty value disables the password."); } }
        public static string WebserverAllowedhostnamesDescription { get { return LC.L(@"The hostnames that are accepted, separated with semicolons. If any of the hostnames are ""*"", all hostnames are allowed and the hostname checking is disabled."); } }
        public static string PingpongkeepaliveShort { get { return LC.L(@"Enables the ping-pong responder"); } }
        public static string PingpongkeepaliveLong { get { return LC.L(@"When running as a server, the service daemon must verify that the process is responding. If this option is enabled, the server reads stdin and writes a reply to each line read"); } }
        public static string LogretentionShort { get { return LC.L(@"Clean up old log data"); } }
        public static string LogretentionLong { get { return LC.L(@"Set the time after which log data will be purged from the database."); } }
        public static string ServerdatafolderShort { get { return LC.L(@"Sets the folder where settings are stored"); } }
        public static string ServerdatafolderLong(string envname) { return LC.L(@"Duplicati needs to store a small database with all settings. Use this option to choose where the settings are stored. This option can also be set with the environment variable {0}.", envname); }
        public static string ServerencryptionkeyShort { get { return LC.L(@"Sets the database encryption key"); } }
        public static string ServerencryptionkeyLong(string envname, string decryptionoption) { return LC.L(@"This option sets the encryption key used to scramble the local settings database. This option can also be set with the environment variable {0}. Use the option --{1} to disable the database scrambling.", envname, decryptionoption); }
        public static string TempdirShort { get { return LC.L(@"Temporary storage folder"); } }
        public static string TempdirLong { get { return LC.L(@"This option can be used to supply an alternative folder for temporary storage. By default the system default temporary folder is used. Note that also SQLite will put temporary files in this temporary folder."); } }
}
    internal static class Scheduler {
        public static string InvalidTimeSetupError(System.DateTime startdate, string interval, string alloweddays) { return LC.L(@"Unable to find a valid date, given the start date {0}, the repetition interval {1} and the allowed days {2}", startdate, interval, alloweddays); }
    }
    internal static class Server
    {
        public static string DefectSSLCertInDatabase { get { return @"Unable to create SSL certificate using data from database. Starting without SSL."; } }
        public static string StartedServer(string ip, int port) { return LC.L(@"Server has started and is listening on {0}, port {1}", ip, port); }
        public static string SSLCertificateFailure(string errormessage) { return LC.L(@"Unable to create SSL certificate using provided parameters. Exception detail: {0}", errormessage); }
        public static string ServerStartFailure(IEnumerable<int> portstried) { return LC.L(@"Unable to open a socket for listening, tried ports: {0}", string.Join(",", from n in (portstried ?? new int[0]) select n.ToString())); }
    }

}
