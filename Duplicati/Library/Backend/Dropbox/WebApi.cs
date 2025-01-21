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

using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.WebApi
{
    public static class Dropbox
    {
        public static string CreateFolderUrl()
        {
            return Uri.UriBuilder(Url.API, Path.CreateFolder);
        }

        public static string ListFilesUrl()
        {
            return Uri.UriBuilder(Url.API, Path.ListFolder);
        }

        public static string ListFilesContinueUrl()
        {
            return Uri.UriBuilder(Url.API, Path.ListFolderContinue);
        }

        public static string DeleteUrl()
        {
            return Uri.UriBuilder(Url.API, Path.DeleteFolder);
        }

        public static string UploadSessionStartUrl()
        {
            return Uri.UriBuilder(Url.CONTENT_API_URL, Path.UploadSessionStart);
        }

        public static string UploadSessionAppendUrl()
        {
            return Uri.UriBuilder(Url.CONTENT_API_URL, Path.UploadSessionAppend);
        }

        public static string UploadSessionFinishUrl()
        {
            return Uri.UriBuilder(Url.CONTENT_API_URL, Path.UploadSessionFinish);
        }

        public static string DownloadFilesUrl()
        {
            return Uri.UriBuilder(Url.CONTENT_API_URL, Path.DownloadFiles);
        }

        public static string[] Hosts()
        {
            return new[] { new System.Uri(Url.API).Host, new System.Uri(Url.CONTENT_API_URL).Host };

        }

        private static class Url
        {
            public const string API = "https://api.dropboxapi.com/2";
            public const string CONTENT_API_URL = "https://content.dropboxapi.com/2";            
        }

        private static class Path
        {
            public const string CreateFolder = "files/create_folder";
            public const string DeleteFolder = "files/delete";
            public const string ListFolder = "files/list_folder";
            public const string ListFolderContinue = "files/list_folder/continue";

            public const string UploadSessionStart = "files/upload_session/start";
            public const string UploadSessionAppend = "files/upload_session/append_v2";
            public const string UploadSessionFinish = "files/upload_session/finish";

            public const string DownloadFiles = "files/download";
        }

    }
}