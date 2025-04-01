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
namespace Duplicati.Library.Interface.Strings
{
    internal static class CommandLineArgument
    {
        public static string AliasesHeader { get { return LC.L(@"aliases"); } }
        public static string DefaultValueHeader { get { return LC.L(@"default value"); } }
        public static string DeprecationMarker { get { return LC.L(@"[DEPRECATED]"); } }
        public static string ValuesHeader { get { return LC.L(@"values"); } }
    }
    internal static class DataTypes
    {
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
    internal static class Common
    {
        public static string FolderAlreadyExistsError { get { return LC.L(@"The folder cannot be created because it already exists"); } }
        public static string FolderMissingError { get { return LC.L(@"The requested folder does not exist"); } }
        public static string CancelExceptionError { get { return LC.L(@"Cancelled"); } }
        public static string SettingsKeyMismatchExceptionError { get { return LC.L(@"Encryption key used to encrypt target settings does not match current key."); } }
        public static string SettingsKeyMissingExceptionError { get { return LC.L(@"Encryption key is missing."); } }
    }

}
