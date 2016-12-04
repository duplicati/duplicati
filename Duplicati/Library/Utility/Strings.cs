using Duplicati.Library.Localization.Short;
using System;


namespace Duplicati.Library.Utility.Strings {
    internal static class Sizeparser {
        public static string InvalidSizeValueError(string value) { return LC.L(@"Invalid size value: {0}", value); }
    }
    internal static class SslCertificateValidator {
        public static string InvalidCallSequence { get { return LC.L(@"The SSL certificate validator was called in an incorrect order"); } }
        public static string MonoHelpSSL { get { return LC.L(@"{0}You may want to import a set of trusted certificates into the Mono certificate store.{0}Use the command:{0}    cert-sync /etc/ssl/certs/ca-certificates.crt #for Debian based systems{0}    cert-sync /etc/pki/tls/certs/ca-bundle.crt #for RedHat derivatives{0}Read more: {1}", Environment.NewLine, "http://www.mono-project.com/docs/about-mono/releases/3.12.0/#cert-sync"); } }
        public static string VerifyCertificateException(System.Net.Security.SslPolicyErrors error, string hash) { return LC.L(@"The server certificate had the error {0} and the hash {1}{2}If you trust this certificate, use the commandline option --accept-specified-ssl-hash={1} to accept the server certificate anyway.{2}You can also attempt to import the server certificate into your operating systems trust pool.", error, hash, Environment.NewLine); }
        public static string VerifyCertificateHashError(System.Exception exception, System.Net.Security.SslPolicyErrors error) { return LC.L(@"Failed while validating certificate hash, error message: {0}, SSL error name: {1}", exception, error); }
    }
    internal static class TempFolder {
        public static string TempFolderDoesNotExistError(string path) { return LC.L(@"Temporary folder does not exist: {0}", path); }
    }
    internal static class Timeparser {
        public static string InvalidIntegerError(string segment) { return LC.L(@"Failed to parse the segment: {0}, invalid integer", segment); }
        public static string InvalidSpecifierError(char specifier) { return LC.L(@"Invalid specifier: {0}", specifier); }
        public static string UnparsedDataFragmentError(string data) { return LC.L(@"Unparsed data: {0}", data); }
    }
    internal static class Uri {
        public static string UriParseError(string uri) { return LC.L(@"The Uri is invalid: {0}", uri); }
        public static string NoHostname(string uri) { return LC.L(@"The Uri is missing a hostname: {0}", uri); }
    }
    internal static class Utility {
        public static string FormatStringB(long size) { return LC.L(@"{0} bytes", size); }
        public static string FormatStringGB(double size) { return LC.L(@"{0:N} GB", size); }
        public static string FormatStringKB(double size) { return LC.L(@"{0:N} KB", size); }
        public static string FormatStringMB(double size) { return LC.L(@"{0:N} MB", size); }
        public static string FormatStringTB(double size) { return LC.L(@"{0:N} TB", size); }
        public static string InvalidDateError(string data) { return LC.L(@"The string ""{0}"" could not be parsed into a date", data); }
    }
    internal static class MD5CalculatingStream {
        public static string IncorrectUsageError { get { return LC.L(@"Cannot read and write on the same stream"); } }
    }
}
