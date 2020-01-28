﻿//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

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