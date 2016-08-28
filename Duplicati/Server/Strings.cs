using Duplicati.Library.Localization.Short;
namespace Duplicati.Server.Strings {
    internal static class Program {
        public static string AnotherInstanceDetected { get { return LC.L(@"Another instance is running, and was notified"); } }
        public static string DatabaseOpenError(string message) { return LC.L(@"Failed to create, open or upgrade the database.
Error message: {0}", message); }
        public static string HelpCommandDescription { get { return LC.L(@"Displays this help"); } }
        public static string HelpDisplayDialog { get { return LC.L(@"Supported commandline arguments:

"); } }
        public static string HelpDisplayFormat(string optionname, string optiontext) { return LC.L(@"--{0}: {1}", optionname, optiontext); }
        public static string LogfileCommandDescription { get { return LC.L(@"Outputs log information to the file given"); } }
        public static string LoglevelCommandDescription { get { return LC.L(@"Determines the amount of information written in the log file"); } }
        public static string PortablemodeCommandDescription { get { return LC.L(@"Activates portable mode where the database is placed below the program executable"); } }
        public static string SeriousError(string message) { return LC.L(@"A serious error occured in Duplicati: {0}", message); }
        public static string StartupFailure(System.Exception error) { return LC.L(@"Unable to start up, perhaps another process is already running?
Error message: {0}", error); }
        public static string UnencrypteddatabaseCommandDescription { get { return LC.L(@"Disables database encryption"); } }
        public static string WrongSQLiteVersion(System.Version actualversion, string expectedversion) { return LC.L(@"Unsupported version of SQLite detected ({0}), must be {1} or higher", actualversion, expectedversion); }
        public static string WebserverWebrootDescription { get { return LC.L(@"The path to the folder where the static files for the webserver is present. The folder must be located beneath the installation folder"); } }
        public static string WebserverPortDescription { get { return LC.L(@"The port the webserver listens on. Multiple values may be supplied with a comma in between."); } }
        public static string WebserverCertificateFileDescription { get { return LC.L(@"The certificate and key file in PKCS #12 format the webserver use for SSL."); } }
        public static string WebserverCertificatePasswordDescription { get { return LC.L(@"The password for decryption of certificate PKCS #12 file."); } }
        public static string WebserverInterfaceDescription { get { return LC.L(@"The interface the webserver listens on. The special values ""*"" and ""any"" means any interface. The special value ""loopback"" means the loopback adapter."); } }
        public static string WebserverPasswordDescription { get { return LC.L(@"The password required to access the webserver. This option is saved so you do not need to set it on each run. Setting an empty value disables the password."); } }
        public static string PingpongkeepaliveShort { get { return LC.L(@"Enables the ping-pong responder"); } }
        public static string PingpongkeepaliveLong { get { return LC.L(@"When running as a server, the service daemon must verify that the process is responding. If this option is enabled, the server reads stdin and writes a reply to each line read"); } }
        public static string LogretentionShort { get { return LC.L(@"Clean up old log data"); } }
        public static string LogretentionLong { get { return LC.L(@"Set the time after which log data will be purged from the database."); } }
        public static string ServerdatafolderShort { get { return LC.L(@"Sets the folder where settings are stored"); } }
        public static string ServerdatafolderLong(string envname) { return LC.L(@"Duplicati needs to store a small database with all settings. Use this option to choose where the settings are stored. This option can also be set with the environment variable {0}.", envname); }
        public static string ServerencryptionkeyShort { get { return LC.L(@"Sets the database encryption key"); } }
        public static string ServerencryptionkeyLong(string envname, string decryptionoption) { return LC.L(@"This option sets the encryption key used to scramble the local settings database. This option can also be set with the environment variable {0}. Use the option --{1} to disable the database scrambling.", envname, decryptionoption); }
    }
    internal static class TaskType {
        public static string FullBackup { get { return LC.L(@"Full backup"); } }
        public static string IncrementalBackup { get { return LC.L(@"Incremental backup"); } }
        public static string ListActualFiles { get { return LC.L(@"List actual files"); } }
        public static string ListBackupEntries { get { return LC.L(@"List backup entries"); } }
        public static string ListBackups { get { return LC.L(@"List backups"); } }
		public static string CompactRemoteFiles { get { return LC.L(@"Compact remote files"); } }
	}
    internal static class Scheduler {
        public static string InvalidTimeSetupError(System.DateTime startdate, string interval, string alloweddays) { return LC.L(@"Unable to find a valid date, given the start date {0}, the repetition interval {1} and the allowed days {2}", startdate, interval, alloweddays); }
    }
}
