using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class OneDrive {
        public static string AuthorizationFailure(string message, string url) { return LC.L(@"Failed to authorize using the WLID service: {0}. If the problem persists, try generating a new authid token from: {1}", message, url); }
        public static string AutoCreatedFolderLabel { get { return LC.L(@"Autocreated folder"); } }
        public static string UnexpectedError(System.Net.HttpStatusCode statuscode, string description) { return LC.L(@"Unexpected error code: {0} - {1}", statuscode, description); }
        public static string MissingFolderError(string folder) { return LC.L(@"Missing the folder: {0}", folder); }
        public static string FileNotFoundError(string name) { return LC.L(@"File not found: {0}", name); }
        public static string DisplayName { get { return LC.L(@"Microsoft OneDrive"); } }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string Description(string mssadescription, string mssalink, string msopdescription, string msoplink) { return LC.L(@"Stores files on Microsoft OneDrive. Usage of this backend requires that you agree to the terms in {0} ({1}) and {2} ({3})", mssadescription, mssalink, msopdescription, msoplink); }

    }
}

