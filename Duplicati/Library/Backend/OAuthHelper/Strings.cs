using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Strings
{
    public static class OAuthHelper {
        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID, you can get it from: {0}"); }
        public static string AuthorizationFailure(string message, string url) { return LC.L(@"Failed to authorize using the OAuth service: {0}. If the problem persists, try generating a new authid token from: {1}", message, url); }
        public static string UnexpectedError(System.Net.HttpStatusCode statuscode, string description) { return LC.L(@"Unexpected error code: {0} - {1}", statuscode, description); }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string OverQuotaError { get { return LC.L(@"The OAuth service is currently over quota, try again in a few hours"); } }
            }
}

