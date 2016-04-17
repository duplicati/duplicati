using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Interface.Strings {
    internal static class CommandLineArgument {
        public static string AliasesHeader { get { return LC.L(@"aliases"); } }
        public static string DefaultValueHeader { get { return LC.L(@"default value"); } }
        public static string DeprecationMarker { get { return LC.L(@"[DEPRECATED]"); } }
        public static string ValuesHeader { get { return LC.L(@"values"); } }
    }
    internal static class DataTypes {
        public static string Boolean { get { return LC.L(@"Boolean"); } }
        public static string Enumeration { get { return LC.L(@"Enumeration"); } }
        public static string Flags { get { return LC.L(@"Flags"); } }
        public static string Integer { get { return LC.L(@"Integer"); } }
        public static string Path { get { return LC.L(@"Path"); } }
        public static string Size { get { return LC.L(@"Size"); } }
        public static string String { get { return LC.L(@"String"); } }
        public static string Timespan { get { return LC.L(@"Timespan"); } }
        public static string Unknown { get { return LC.L(@"Unknown"); } }
    }
    internal static class Common {
        public static string ConfigurationIsMissingItemError(string fieldname) { return LC.L(@"The configuration for the backend is not valid, it is missing the {0} field", fieldname); }
        public static string ConfirmTestConnectionQuestion { get { return LC.L(@"Do you want to test the connection?"); } }
        public static string ConnectionFailure(string message) { return LC.L(@"Connection Failed: {0}", message); }
        public static string ConnectionSuccess { get { return LC.L(@"Connection succeeded!"); } }
        public static string DefaultDirectoryWarning { get { return LC.L(@"You have not entered a path. This will store all backups in the default directory. Is this what you want?"); } }
        public static string EmptyPasswordError { get { return LC.L(@"You must enter a password"); } }
        public static string EmptyPasswordWarning { get { return LC.L(@"You have not entered a password.
Proceed without a password?"); } }
        public static string EmptyServernameError { get { return LC.L(@"You must enter the name of the server"); } }
        public static string EmptyUsernameError { get { return LC.L(@"You must enter a username"); } }
        public static string EmptyUsernameWarning { get { return LC.L(@"You have not entered a username.
This is fine if the server allows anonymous uploads, but likely a username is required
Proceed without a username?"); } }
        public static string ExistingBackupDetectedQuestion { get { return LC.L(@"The connection succeeded but another backup was found in the destination folder. It is possible to configure Duplicati to store multiple backups in the same folder, but it is not recommended.

Do you want to use the selected folder?"); } }
        public static string FolderAlreadyExistsError { get { return LC.L(@"The folder cannot be created because it already exists"); } }
        public static string FolderCreated { get { return LC.L(@"Folder created!"); } }
        public static string FolderMissingError { get { return LC.L(@"The requested folder does not exist"); } }
        public static string InvalidServernameError(string servername) { return LC.L(@"The server name ""{0}"" is not valid", servername); }
        public static string CancelExceptionError { get { return LC.L(@"Cancelled"); } }
    }
}
