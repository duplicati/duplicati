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
namespace Duplicati.Library.Backend.Strings
{
    internal static class SharePoint
    {
        public static string Description => LC.L(@"This backend can read and write data to a SharePoint server (including OneDrive for Business). Allowed formats are ""mssp://tennant.sharepoint.com/PathToWeb//BaseDocLibrary/subfolder"" and ""mssp://username:password@tennant.sharepoint.com/PathToWeb//BaseDocLibrary/subfolder"". Use a double slash '//' in the path to denote the web from the documents library.");
        public static string DisplayName => LC.L(@"Microsoft SharePoint");
        public static string DescriptionIntegratedAuthenticationLong => LC.L(@"If the server and client both supports integrated authentication, this option enables that authentication method. This is likely only available with windows servers and clients.");
        public static string DescriptionIntegratedAuthenticationShort => LC.L(@"Use windows integrated authentication to connect to the server");
        public static string DescriptionUseRecyclerLong => LC.L(@"Use this option to have files moved to the recycle bin folder instead of removing them permanently when compacting or deleting backups.");
        public static string DescriptionUseRecyclerShort => LC.L(@"Move deleted files to the recycle bin");

        public static string DescriptionBinaryDirectModeLong => LC.L(@"Use this option to upload files to SharePoint as a whole with BinaryDirect mode. This is the most efficient way of uploading, but can cause non-recoverable timeouts under certain conditions. Use this option only with very fast and stable internet connections.");
        public static string DescriptionBinaryDirectModeShort => LC.L(@"Upload files using binary direct mode");

        public static string DescriptionWebTimeoutLong => LC.L(@"Use this option to specify a custom value for timeouts of web operation when communicating with SharePoint Server. Recommended value is 180s.");
        public static string DescriptionWebTimeoutShort => LC.L(@"Set timeout for SharePoint web operations");

        public static string DescriptionChunkSizeLong => LC.L(@"Use this option to specify the size of each chunk when uploading to SharePoint Server. Recommended value is 4MB.");
        public static string DescriptionChunkSizeShort => LC.L(@"Set block size for chunked uploads to SharePoint");

        public static string MissingElementError(string serverrelpath, string? hosturl) { return LC.L(@"Element with path '{0}' not found on host '{1}'.", serverrelpath, hosturl); }
        public static string NoSharePointWebFoundError(string url) { return LC.L(@"No SharePoint web could be logged in to at path '{0}'. Maybe wrong credentials. Or try using '//' in path to separate web from folder path.", url); }
        public static string WebTitleReadFailedError => LC.L(@"Everything seemed alright, but then web title could not be read to test connection. Something's wrong.");
    }

    internal static class OneDriveForBusiness
    {
        public static string Description => LC.L(@"This backend can read and write data to Microsoft OneDrive for Business. Allowed formats are ""od4b://tennant.sharepoint.com/personal/username_domain/Documents/subfolder"" and ""od4b://username:password@tennant.sharepoint.com/personal/username_domain/Documents/folder"". You can use a double slash '//' in the path to denote the base path from the documents folder.");
        public static string DisplayName => LC.L(@"Microsoft OneDrive for Business");
    }
}
