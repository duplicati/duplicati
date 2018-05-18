//  Copyright (C) 2018, The Duplicati Team
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
using System.Collections.Specialized;

namespace Duplicati.Library.Backend.WebApi
{
    class Google
    {

        public class QueryParam
        {
            public const string File = "q";
            public const string PageToken = "pageToken";
            public const string UploadType = "uploadType";
            public const string Alt = "alt";
        }

        public class QueryValue
        {
            public const string True = "true";
            public const string Resumable = "resumable";
            public const string Media = "media";
        }
    }


    class GoogleCloudServices : Google
    {
        public static class Url {
            public const string API = "https://www.googleapis.com/storage/v1";
            public const string UPLOAD = "https://www.googleapis.com/upload/storage/v1";    
        }

        public new class QueryParam : Google.QueryParam
        {
            public const string Project = "project";
            public const string Prefix = "prefix";                        
        }

        public static class Path
        {
            public const string Bucket = "b";
            public const string Object = "o";
        }


    }

    class GoogleDrive : Google
    {
        public static class Url
        {
            public const string DRIVE = "https://www.googleapis.com/drive/v2";
            public const string UPLOAD = "https://www.googleapis.com/upload/drive/v2";
        }

        public static class Path
        {
            public const string File = "files";
            public const string About = "about";
        }

        public new class QueryParam : Google.QueryParam
        {
            public const string SupportsTeamDrive = "supportsTeamDrives";
            public const string IncludeTeamDrive = "includeTeamDriveItems";
        }

        public static string FileQueryUrl(NameValueCollection values)
        {
            return Library.Utility.Uri.UriBuilder(Url.DRIVE, Path.File, values);
        }

        public static string FileQueryUrl(string fileId, NameValueCollection values = null)
        {
            return Library.Utility.Uri.UriBuilder(Url.DRIVE,
                                                  Library.Utility.UrlPath.Create(Path.File).Append(fileId).ToString(),
                                                  values);
        }

        public static string FileUploadUrl(string fileId, NameValueCollection values)
        {
            return Library.Utility.Uri.UriBuilder(Url.UPLOAD, 
                                                  Library.Utility.UrlPath.Create(Path.File).Append(fileId).ToString(),
                                                  values);
        }

        public static string FileUploadUrl(NameValueCollection values)
        {
            return Library.Utility.Uri.UriBuilder(Url.UPLOAD, Path.File, values);
        }
    }
}