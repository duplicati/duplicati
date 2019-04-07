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
using System.Collections.Generic;
using System.Collections.Specialized;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.WebApi
{
    public static class GoogleCloudStorage
    {
        // From: https://cloud.google.com/storage/docs/bucket-locations
        public static readonly KeyValuePair<string, string>[] KNOWN_GCS_LOCATIONS = {
            new KeyValuePair<string, string>("(default)", null),
            new KeyValuePair<string, string>("Europe", "EU"),
            new KeyValuePair<string, string>("United States", "US"),
            new KeyValuePair<string, string>("Asia", "ASIA"),

            //Regional buckets: https://cloud.google.com/storage/docs/regional-buckets
            new KeyValuePair<string, string>("Eastern Asia-Pacific", "ASIA-EAST1"),
            new KeyValuePair<string, string>("Central United States 1", "US-CENTRAL1"),
            new KeyValuePair<string, string>("Central United States 2", "US-CENTRAL2"),
            new KeyValuePair<string, string>("Eastern United States 1", "US-EAST1"),
            new KeyValuePair<string, string>("Eastern United States 2", "US-EAST2"),
            new KeyValuePair<string, string>("Eastern United States 3", "US-EAST3"),
            new KeyValuePair<string, string>("Western United States", "US-WEST1"),
        };


        public static readonly KeyValuePair<string, string>[] KNOWN_GCS_STORAGE_CLASSES = {
            new KeyValuePair<string, string>("(default)", null),
            new KeyValuePair<string, string>("Standard", "STANDARD"),
            new KeyValuePair<string, string>("Durable Reduced Availability (DRA)", "DURABLE_REDUCED_AVAILABILITY"),
            new KeyValuePair<string, string>("Nearline", "NEARLINE"),
        };

        public static string[] Hosts()
        {
            return new [] { new System.Uri(Url.UPLOAD).Host,
                new System.Uri(Url.API).Host };
        }

        public static string DeleteUrl(string bucketId, string objectId)
        {
            var path = BucketObjectPath(bucketId, objectId);

            return Uri.UriBuilder(Url.API, path);
        }

        public static string CreateFolderUrl(string projectId)
        {
            var queryParams = new NameValueCollection
            {
                { QueryParam.Project, projectId }
            };

            return Uri.UriBuilder(Url.API, Path.Bucket, queryParams);
        }

        public static string RenameUrl(string bucketId, string objectId)
        {
            return Uri.UriBuilder(Url.API, BucketObjectPath(bucketId, objectId));
        }

        public static string ListUrl(string bucketId, string prefix)
        {
            return ListUrl(bucketId, prefix, null);
        }

        public static string ListUrl(string bucketId, string prefix, string token)
        {
            var queryParams = new NameValueCollection {
                    { QueryParam.Prefix, prefix }
                };

            if (token != null)
            {
                queryParams.Set(QueryParam.PageToken, token);
            }

            return Uri.UriBuilder(Url.API, BucketObjectPath(bucketId), queryParams);
        }

        public static string PutUrl(string bucketId)
        {
            var queryParams = new NameValueCollection
            {
                { QueryParam.UploadType, QueryValue.Resumable }
            };
            var path = UrlPath.Create(Path.Bucket).Append(bucketId).Append(Path.Object).ToString();
            return Uri.UriBuilder(Url.UPLOAD, path, queryParams);
        }

        public static string GetUrl(string bucketId, string objectId)
        {
            var queryParams = new NameValueCollection
                {
                    { QueryParam.Alt
                            , QueryValue.Media }
                };
            var path = BucketObjectPath(bucketId, objectId);

            return Uri.UriBuilder(Url.API, path, queryParams);
        }

        private static class Url
        {
            public const string API = "https://www.googleapis.com/storage/v1";
            public const string UPLOAD = "https://www.googleapis.com/upload/storage/v1";
        }

        private static class QueryParam
        {
            public const string Project = "project";
            public const string Prefix = "prefix";
            public const string PageToken = "pageToken";
            public const string UploadType = "uploadType";
            public const string Alt = "alt";
        }

        private static class QueryValue
        {
            public const string Resumable = "resumable";
            public const string Media = "media";
        }

        private static class Path
        {
            public const string Bucket = "b";
            public const string Object = "o";
        }

        private static string BucketObjectPath(string bucketId, string objectId = null)
        {
            return UrlPath.Create(Path.Bucket)
                          .Append(bucketId)
                          .Append(Path.Object)
                          .Append(objectId).ToString();
        }
    }

    public static class GoogleDrive
    {
        public static string[] Hosts()
        {
            return new [] { new System.Uri(Url.DRIVE).Host, new System.Uri(Url.UPLOAD).Host };

        }

        public static string GetUrl(string fileId)
        {
            return FileQueryUrl(fileId, new NameValueCollection{
                { QueryParam.Alt, QueryValue.Media }
            });
        }

        public static string RenameUrl(string fileId)
        {
            return FileQueryUrl(Uri.UrlPathEncode(fileId));
        }

        public static string DeleteUrl(string fileId, string teamDriveId)
        {
            return FileQueryUrl(Uri.UrlPathEncode(fileId), AddTeamDriveParam(teamDriveId));
        }

        public static string PutUrl(string fileId, bool useTeamDrive)
        {
            var queryParams = new NameValueCollection {
                { QueryParam.UploadType,
                    QueryValue.Resumable } };

            if (useTeamDrive)
            {
                queryParams.Add(QueryParam.SupportsTeamDrive, QueryValue.True);
            }

            return !string.IsNullOrWhiteSpace(fileId) ?
                FileUploadUrl(Uri.UrlPathEncode(fileId), queryParams) :
                      FileUploadUrl(queryParams);
        }

        public static string ListUrl(string fileQuery, string teamDriveId)
        {
            return ListUrl(fileQuery, teamDriveId, null);
        }
        
        public static string ListUrl(string fileQuery, string teamDriveId, string token)
        {
            var queryParams = new NameValueCollection
            {
                { QueryParam.File,
                    fileQuery }
            };

            queryParams.Add(AddTeamDriveParam(teamDriveId));
            
            if (token != null)
            {
                queryParams.Set(QueryParam.PageToken, token);
            }

            return FileQueryUrl(queryParams);
        }

        public static string CreateFolderUrl(string teamDriveId)
        {
            return FileQueryUrl(AddTeamDriveParam(teamDriveId));
        }

        public static string AboutInfoUrl()
        {
            return Uri.UriBuilder(Url.DRIVE, Path.About);
        }

        private static class Url
        {
            public const string DRIVE = "https://www.googleapis.com/drive/v2";
            public const string UPLOAD = "https://www.googleapis.com/upload/drive/v2";
        }

        private static class Path
        {
            public const string File = "files";
            public const string About = "about";
        }

        private static class QueryParam
        {
            public const string SupportsTeamDrive = "supportsTeamDrives";
            public const string IncludeTeamDrive = "includeTeamDriveItems";
            public const string TeamDriveId = "teamDriveId";
            public const string corpora = "corpora";
            public const string File = "q";
            public const string PageToken = "pageToken";
            public const string UploadType = "uploadType";
            public const string Alt = "alt";
        }

        private static class QueryValue
        {
            public const string True = "true";
            public const string Resumable = "resumable";
            public const string Media = "media";
            public const string TeamDrive = "teamDrive";
        }

        private static string FileQueryUrl(NameValueCollection values)
        {
            return Uri.UriBuilder(Url.DRIVE, Path.File, values);
        }

        private static string FileQueryUrl(string fileId, NameValueCollection values = null)
        {
            return Uri.UriBuilder(Url.DRIVE, UrlPath.Create(Path.File).Append(fileId).ToString(),
                                  values);
        }

        private static string FileUploadUrl(string fileId, NameValueCollection values)
        {
            return Uri.UriBuilder(Url.UPLOAD, UrlPath.Create(Path.File).Append(fileId).ToString(),
                                  values);
        }

        private static string FileUploadUrl(NameValueCollection values)
        {
            return Uri.UriBuilder(Url.UPLOAD, Path.File, values);
        }

        private static NameValueCollection AddTeamDriveParam(string teamDriveId)
        {
            return teamDriveId != null ? new NameValueCollection {
                { WebApi.GoogleDrive.QueryParam.SupportsTeamDrive,
                    WebApi.GoogleDrive.QueryValue.True },
                { WebApi.GoogleDrive.QueryParam.TeamDriveId,  
                    teamDriveId },
                { WebApi.GoogleDrive.QueryParam.IncludeTeamDrive,
                    WebApi.GoogleDrive.QueryValue.True },
                { WebApi.GoogleDrive.QueryParam.corpora,
                    WebApi.GoogleDrive.QueryValue.TeamDrive }

            } : new NameValueCollection( );
        }
    }
}