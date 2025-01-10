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
}
