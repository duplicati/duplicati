using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.SQLiteHelper.Strings {
    internal static class DatabaseUpgrader {
        public static string BackupFilenamePrefix { get { return LC.L(@"backup"); } }
        public static string DatabaseFormatError(string message) { return LC.L(@"Unable to determine database format: {0}", message); }
        public static string InvalidVersionError(int actualversion, int maxversion, string backupfolder) { return LC.L(@"
The database has version {0} but the largest supported version is {1}.

This is likely caused by upgrading to a newer version and then downgrading.
If this is the case, there is likely a backup file of the previous database version in the folder {2}.", actualversion, maxversion, backupfolder); }
        public static string TableLayoutError { get { return LC.L(@"Unknown table layout detected"); } }
        public static string UpgradeFailure(string sql, string message) { return LC.L(@"Failed to execute SQL: {0}
Error: {1}
Database is NOT upgraded.", sql, message); }
    }
}
