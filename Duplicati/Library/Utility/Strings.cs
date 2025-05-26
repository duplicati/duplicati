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
using Duplicati.Library.Localization.Short;
using System;


namespace Duplicati.Library.Utility.Strings
{
    internal static class Sizeparser
    {
        public static string InvalidSizeValueError(string value) { return LC.L(@"Invalid size value: {0}", value); }
    }
    internal static class SslCertificateValidator
    {
        public static string InvalidCallSequence { get { return LC.L(@"The SSL certificate validator was called in an incorrect order"); } }
        public static string VerifyCertificateException(System.Net.Security.SslPolicyErrors error, string hash) { return LC.L(@"The server certificate had the error {0} and the hash {1}{2}If you trust this certificate, use the commandline option --{3}={1} to accept the server certificate anyway.{2}You can also attempt to import the server certificate into your operating systems trust pool.", error, hash, Environment.NewLine, "accept-specified-ssl-hash"); }
        public static string VerifyCertificateHashError(System.Exception exception, System.Net.Security.SslPolicyErrors error) { return LC.L(@"Failed while validating certificate hash, error message: {0}, SSL error name: {1}", exception, error); }
    }
    internal static class TempFolder
    {
        public static string TempFolderDoesNotExistError(string path) { return LC.L(@"Temporary folder does not exist: {0}", path); }
    }
    internal static class Timeparser
    {
        public static string InvalidIntegerError(string segment) { return LC.L(@"Failed to parse the segment: {0}, invalid integer", segment); }
        public static string InvalidSpecifierError(char specifier) { return LC.L(@"Invalid specifier: {0}", specifier); }
        public static string UnparsedDataFragmentError(string data) { return LC.L(@"Unparsed data: {0}", data); }
        public static string InvalidDateTimeError(string data) { return LC.L(@"The string ""{0}"" could not be parsed into a DateTime", data); }
    }
    internal static class Uri
    {
        public static string UriParseError(string uri) { return LC.L(@"The Uri is invalid: {0}", uri); }
        public static string NoHostname(string uri) { return LC.L(@"The Uri is missing a hostname: {0}", uri); }
    }
    internal static class Utility
    {
        public static string FormatStringB(long size) { return LC.L(@"{0} bytes", size); }
        public static string FormatStringGB(double size) { return LC.L(@"{0:N} GiB", size); }
        public static string FormatStringKB(double size) { return LC.L(@"{0:N} KiB", size); }
        public static string FormatStringMB(double size) { return LC.L(@"{0:N} MiB", size); }
        public static string FormatStringTB(double size) { return LC.L(@"{0:N} TiB", size); }
        public static string InvalidDateError(string data) { return LC.L(@"The string ""{0}"" could not be parsed into a date", data); }
    }
    internal static class HashCalculatingStream
    {
        public static string IncorrectUsageError { get { return LC.L(@"Cannot read and write on the same stream"); } }
    }
    internal static class Filters
    {
        public static string UnknownFilterGroup(string filterSet) { return LC.L(@"The string {0} does not represent a known filter group name. Valid values are: {1}", filterSet, string.Join(", ", Enum.GetNames(typeof(FilterGroup)))); }
    }

    internal static class TimeoutSettingsHelper
    {
        public static string DescriptionShortTimeoutLong { get { return LC.L(@"The timeout in seconds for short operations like delete and create folder"); } }
        public static string DescriptionShortTimeoutShort { get { return LC.L(@"Short operation timeout"); } }
        public static string DescriptionListTimeoutLong { get { return LC.L(@"The timeout in seconds for listing files and folders"); } }
        public static string DescriptionListTimeoutShort { get { return LC.L(@"List operation timeout"); } }
        public static string DescriptionReadWriteTimeoutLong { get { return LC.L(@"The timeout in seconds for read and write operations. If no activity is detected in this interval, a timeout error is raised"); } }
        public static string DescriptionReadWriteTimeoutShort { get { return LC.L(@"Read/write operation timeout"); } }
    }

    internal static class SslOptionsHelper
    {
        public static string DescriptionAcceptAnyCertificateLong { get { return LC.L(@"Use this option to accept any server certificate, regardless of what errors it may have. Please use --{0} instead, whenever possible.", "accept-specified-ssl-hash"); } }
        public static string DescriptionAcceptAnyCertificateShort { get { return LC.L(@"Accept any server certificate"); } }
        public static string DescriptionAcceptHashLong { get { return LC.L(@"If your server certificate is reported as invalid (e.g. with self-signed certificates), you can supply the certificate hash (SHA1) to approve it anyway. The hash value must be entered in hex format without spaces or colons. You can enter multiple hashes separated by commas."); } }
        public static string DescriptionAcceptHashShort { get { return LC.L(@"Optionally accept a known SSL certificate"); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this option to communicate using Secure Socket Layer (SSL) over http (https)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instruct Duplicati to use an SSL (https) connection"); } }
    }

    internal static class AuthSettingsHelper
    {
        public static string DescriptionAuthPasswordLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string DescriptionAuthPasswordShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string DescriptionAuthUsernameLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string DescriptionAuthUsernameShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string UsernameAndPasswordRequired { get { return LC.L(@"Authentication requires both a username and a password"); } }
    }

    internal static class AuthIdSettingsHelper
    {
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string MissingAuthID(string url) { return LC.L(@"You need an AuthID to use this destination. You can get it from: {0}", url); }

    }

    internal static class CommandLineArgumentValidator
    {
        public static string UnsupportedBooleanValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not parse into a valid boolean. This will be treated as if it was set to ""true""", optionname, value); }
        public static string UnsupportedEnumerationValue(string optionname, string value, string[] values) { return LC.L(@"The option --{0} does not support the value ""{1}"". Supported values are: {2}", optionname, value, string.Join(", ", values)); }
        public static string UnsupportedFlagsValue(string optionname, string value, string[] values) { return LC.L(@"The option --{0} does not support the value ""{1}"". Supported flag values are: {2}", optionname, value, string.Join(", ", values)); }
        public static string UnsupportedIntegerValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid integer", optionname, value); }
        public static string UnsupportedPathValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid path", optionname, value); }
        public static string UnsupportedSizeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid size", optionname, value); }
        public static string UnsupportedTimeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid time", optionname, value); }
        public static string NonQualifiedSizeValue(string optionname, string value) { return LC.L(@"The size ""{1}"" supplied to --{0} does not have a multiplier (b, kb, mb, etc). A multiplier is recommended to avoid unexpected changes if the program is updated.", optionname, value); }
    }

    internal static class BackendExtensions
    {
        public static string ErrorDeleteFile(string filename, string message) { return LC.L(@"Error on deleting file: {0}, error: {1}", filename, message); }
        public static string ErrorReadFile(string filename, string message) { return LC.L(@"Error reading file: {0}, error: {1}", filename, message); }
        public static string ErrorWriteFile(string filename, string message) { return LC.L(@"Error writing file: {0}, error: {1}", filename, message); }
        public static string ErrorListContent(string message) { return LC.L(@"Error listing content: {0}", message); }
    }
}
