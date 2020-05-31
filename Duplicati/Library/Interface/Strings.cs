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
        public static string FolderAlreadyExistsError { get { return LC.L(@"The folder cannot be created because it already exists"); } }
        public static string FolderMissingError { get { return LC.L(@"The requested folder does not exist"); } }
        public static string CancelExceptionError { get { return LC.L(@"Cancelled"); } }
    }
}
