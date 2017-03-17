using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class TahoeBackend {
        public static string Description { get { return LC.L(@"This backend can read and write data to a Tahoe-LAFS based backend. Allowed format is ""tahoe://hostname:port/uri/$DIRCAP""."); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this flag to communicate using Secure Socket Layer (SSL) over http (https)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instructs Duplicati to use an SSL (https) connection"); } }
        public static string Displayname { get { return LC.L(@"Tahoe-LAFS"); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found, message: {1}", foldername, message); }
        public static string UnrecognizedUriError { get { return LC.L(@"Unsupported URL format, must start with ""uri/URI:DIR2:"""); } }
    }
}
