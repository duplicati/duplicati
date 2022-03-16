//  Copyright (C) 2015, The Duplicati Team
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
using Duplicati.Library.Common.IO;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

// Full IDrive Sync API documentation can be found here: https://www.idrivesync.com/evs/web-developers-guide.htm
namespace Duplicati.Library.Backend.IDrive
{
    /// <summary>
    /// Provides access to an IDrive Sync.
    /// </summary>
    public class IDriveApiClient
    {
        private const string IDRIVE_AUTH_CGI_URL = "https://www1.idrive.com/cgi-bin/v1/user-details.cgi";
        private const string IDRIVE_SYNC_GET_SERVER_ADDRESS_URL = "https://evs.idrivesync.com/evs/getServerAddress";
        private const string SUCCESS = "SUCCESS";
        private const string MESSAGE_ATTRIBUTE = "message";
        private const string XML_RESPONSE_TAG = "tree";

        private string _idriveUsername;
        private string _idrivePassword;

        private string _syncUsername;
        private string _syncPassword;
        private string _syncHostname;

        public string UserAgent { get; set; } = "Duplicati-IDrive-API-Client/" + Assembly.GetExecutingAssembly().GetName().Version;

        public IDriveApiClient()
        {
        }

        public async Task LoginAsync(string username, string password)
        {
            _idriveUsername = username;
            _idrivePassword = password;

            await IDriveAuthAsync();
            await UpdateSyncHostnameAsync();
        }

        private async Task IDriveAuthAsync()
        {
            // IDrive auth logic was reverse engineered from code found in the IDriveForLinux PERL scripts provided by IDrive. Download from: https://www.idrive.com/linux-backup-scripts
            // The auth response payload contains the login credentials for the associated IDrive Sync account.
            const string methodName = IDRIVE_AUTH_CGI_URL;
            using (var httpClient = GetHttpClient())
            {
                var parameters = new List<KeyValuePair<string, string>>() {
                    new KeyValuePair<string, string> ( "username", _idriveUsername),
                    new KeyValuePair<string, string> ( "password", _idrivePassword)
                };
                var content = new FormUrlEncodedContent(parameters);
                using (var response = await httpClient.PostAsync(IDRIVE_AUTH_CGI_URL, content))
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new AuthenticationException($"Failed IDrive authentication request. Server response: {response}");

                    // Sample response (masked): "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n\n<root>\n  <login remote_manage_ip=\"173.255.13.30\" quota=\"5000000000000\" datacenter=\"evsvirginia.idrive.com\" enctype=\"DEFAULT\" pns_sync=\"notify2.idrive.com\" remote_manage_server_https=\"wsn16s.idrive.com\" jspsrvr=\"www.idrive.com\" plan_type=\"Regular\" dedup=\"on\" username_sync=\"abc12345678901234def\" password_sync=\"fed09887654321098cba\" dedup_enabled=\"no\" desc=\"Success\" evssrvrip=\"148.51.142.138\" plan=\"Personal\" acctype=\"IBSYNC\" cnfgstat=\"SET\" accstat=\"Y\" evswebsrvr=\"evsweb5114.idrive.com\" remote_manage_websock_server=\"yes\" evssrvr=\"evs5114.idrive.com\" message=\"SUCCESS\" remote_manage_ip_https=\"173.255.13.31\" cnfgstatus=\"Y\" remote_manage_server=\"wsn16.idrive.com\" evswebsrvrip=\"148.51.142.139\" quota_used=\"100000000000\"></login>\n</root>\n"
                    string responseString = await response.Content.ReadAsStringAsync();
                    var responseXml = new XmlDocument();
                    responseXml.LoadXml(responseString);
                    var nodes = responseXml.GetElementsByTagName("login");
                    if (nodes.Count == 0)
                        throw new AuthenticationException($"Failed '{methodName}' request. Unexpected authentication response data (no login element). Server response: {response}");

                    var responseNode = nodes[0];

                    if (responseNode.Attributes[MESSAGE_ATTRIBUTE]?.Value != SUCCESS)
                        throw new AuthenticationException($"Failed IDrive authentication request. Non-{SUCCESS}. Description: {responseNode.Attributes["desc"]?.Value}");

                    _syncUsername = responseNode.Attributes["username_sync"]?.Value;
                    _syncPassword = responseNode.Attributes["password_sync"]?.Value;

                    if (string.IsNullOrEmpty(_syncUsername) || string.IsNullOrEmpty(_syncPassword))
                        throw new AuthenticationException($"Failed '{methodName}' request.  IDrive Sync username and/or password were not provided. Server response: {response}");
                }
            }
        }

        private async Task UpdateSyncHostnameAsync()
        {
            // The API docs state that the sync web API server may change over time and must be retrieved on each login.
            // The server may be different for different accounts, depending where the data is stored.
            var responseNode = await GetSimpleTreeResponseAsync(IDRIVE_SYNC_GET_SERVER_ADDRESS_URL, "getServerAddress");

            _syncHostname = responseNode.Attributes["webApiServer"]?.Value;

            if (string.IsNullOrEmpty(_syncHostname))
                throw new AuthenticationException($"Failed 'getServerAddress' request. Empty hostname. Tree XML: {responseNode.OuterXml}");
        }

        public async Task<List<FileEntry>> GetFileEntryListAsync(string directoryPath, string searchCriteria = null)
        {
            string methodName = string.IsNullOrEmpty(searchCriteria) ? "browseFolder" : "searchFiles";
            // NOTE: The IDrive "searchFiles" API method has a bug that returns the name of the directory being listed as one of the items when searching for "*"
            string url = GetSyncServiceUrl(methodName);
            var list = new List<FileEntry>();

            using (var httpClient = GetHttpClient())
            using (var content = GetSyncPostContent(new NameValueCollection { { "p", directoryPath }, { "searchkey", searchCriteria } }))
            using (var response = await httpClient.PostAsync(url, content))
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new ApplicationException($"Failed '{methodName}' request. Server response: {response}");

                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var xmlReader = XmlReader.Create(responseStream, new XmlReaderSettings() { Async = true }))
                {
                    bool success = false;
                    while (await xmlReader.ReadAsync())
                    {
                        if (xmlReader.Name != XML_RESPONSE_TAG)
                            continue;

                        success = xmlReader.GetAttribute(MESSAGE_ATTRIBUTE) == SUCCESS;
                        break;
                    }

                    if (!success)
                        throw new ApplicationException($"Failed '{methodName}' request. Non-{SUCCESS}. Description: {xmlReader.GetAttribute("desc")}");

                    while (await xmlReader.ReadAsync())
                    {
                        // Item XML example: <item restype="1" resname="Myoffice.txt" size="9583" lmd="2010/05/26 01:58:57" ver="1" thumb="N"/>
                        if (xmlReader.Name != "item")
                            continue;

                        string resname = xmlReader.GetAttribute("resname");
                        if (string.IsNullOrEmpty(resname))
                            continue;

                        string restype = xmlReader.GetAttribute("restype");
                        string size = xmlReader.GetAttribute("size");
                        string lmd = xmlReader.GetAttribute("lmd");

                        long.TryParse(size, out long parsedSize);
                        DateTime.TryParse(lmd, out DateTime parsedModificationDate);

                        var fileEntry = new FileEntry(resname)
                        {
                            IsFolder = restype != "1",
                            Name = resname,
                            Size = parsedSize,
                            LastModification = parsedModificationDate
                        };

                        list.Add(fileEntry);
                    }
                }
            }

            return list;
        }

        public async Task CreateDirectoryAsync(string directoryName, string baseDirectoryPath)
        {
            const string methodName = "createFolder";
            string url = GetSyncServiceUrl(methodName);
            try
            {
                await GetSimpleTreeResponseAsync(url, methodName, new NameValueCollection { { "p", baseDirectoryPath }, { "foldername", directoryName } });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(@"FOLDER ALREADY EXISTS WITH THIS NAME."))
                    return;

                throw;
            }
        }

        public async Task DeleteAsync(string filePath, bool moveToTrash = true)
        {
            const string methodName = "deleteFile";
            string url = GetSyncServiceUrl(methodName);
            await GetSimpleTreeResponseAsync(url, methodName, new NameValueCollection { { "p", filePath }, { "trash", moveToTrash ? "yes" : "no" } });
        }

        public async Task<FileEntry> UploadAsync(Stream stream, string filename, string directoryPath, CancellationToken cancelToken)
        {
            const string methodName = "uploadFile";
            string url = GetSyncServiceUrl(methodName);

            using (var httpClient = GetHttpClient())
            using (var content = GetSyncPostContent(new NameValueCollection { { "p", directoryPath } }, isMultiPart: true))
            {
                ((MultipartFormDataContent)content).Add(new StreamContent(stream), filename, filename);

                using (var response = await httpClient.PostAsync(url, content))
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new ApplicationException($"Failed '{methodName}' request. Server response: {response}");

                    string responseString = await response.Content.ReadAsStringAsync();
                    var responseXml = new XmlDocument();
                    responseXml.LoadXml(responseString);
                    var nodes = responseXml.GetElementsByTagName(XML_RESPONSE_TAG);
                    if (nodes.Count == 0)
                        throw new ApplicationException($"Failed '{methodName}' request. Unexpected response. Server response: {response}");

                    var responseNode = nodes[0];
                    if (responseNode == null)
                        throw new ApplicationException($"Failed '{methodName}' request. Missing {XML_RESPONSE_TAG} node. Server response: {response}");

                    if (responseNode.Attributes[MESSAGE_ATTRIBUTE]?.Value != SUCCESS)
                        throw new ApplicationException($"Failed '{methodName}' request. Non-{SUCCESS}. Description: {responseNode.Attributes["desc"]?.Value}");
                }
            }

            var fileEntryList = await GetFileEntryListAsync(directoryPath, filename);
            return fileEntryList.FirstOrDefault();
        }

        public async Task DownloadAsync(string filePath, Stream stream)
        {
            const string methodName = "downloadFile";
            string url = GetSyncServiceUrl(methodName);

            using (var httpClient = GetHttpClient())
            using (var content = GetSyncPostContent(new NameValueCollection { { "p", filePath } }))
            using (var response = await httpClient.PostAsync(url, content))
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new AuthenticationException($"Failed '{methodName}' request. Server response: {response}");

                response.Headers.TryGetValues("RESTORE_STATUS", out var restoreStatus); // The download API uses RESTORE_STATUS to indicate success instead of body XML

                using (var responseStream = await response.Content.ReadAsStreamAsync())
                {
                    if (restoreStatus.FirstOrDefault() == SUCCESS)
                    {
                        Library.Utility.Utility.CopyStream(responseStream, stream);
                        return;
                    }

                    using (var xmlReader = XmlReader.Create(responseStream, new XmlReaderSettings() { Async = true }))
                    {
                        bool success = false;
                        while (await xmlReader.ReadAsync())
                        {
                            if (xmlReader.Name != XML_RESPONSE_TAG)
                                continue;

                            success = xmlReader.GetAttribute(MESSAGE_ATTRIBUTE) == SUCCESS;
                            break;
                        }

                        if (!success)
                            throw new ApplicationException($"Failed '{methodName}' request. Non-{SUCCESS}. Description: {xmlReader.GetAttribute("desc")}");

                        throw new ApplicationException($"Failed '{methodName}' request. Invalid RESTORE_STATUS result with invalid {SUCCESS} message."); // this should never happen
                    }
                }
            }
        }
        private HttpClient GetHttpClient()
        {
            var httpClient = new HttpClient();

            if (!string.IsNullOrEmpty(UserAgent))
                httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            return httpClient;
        }

        private string GetSyncServiceUrl(string serviceName)
        {
            return $"https://{_syncHostname}/evs/{serviceName}";
        }

        private async Task<XmlNode> GetSimpleTreeResponseAsync(string url, string methodName, NameValueCollection parameters = null)
        {
            using (var httpClient = GetHttpClient())
            using (var content = GetSyncPostContent(parameters))
            using (var response = await httpClient.PostAsync(url, content))
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new ApplicationException($"Failed '{methodName}' request. Server response: {response}");

                // Sample response: "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<tree message=\"SUCCESS\" cmdUtilityServer=\"evs19.idrivesync.com\"\n    cmdUtilityServerIP=\"4.71.135.136\"\n    webApiServer=\"evsweb19.idrivesync.com\" webApiServerIP=\"4.71.135.137\"\n    faceWebApiServer=\"\" faceWebApiServerIP=\"\" dedup=\"off\"/>\n"
                string responseString = await response.Content.ReadAsStringAsync();
                var responseXml = new XmlDocument();
                responseXml.LoadXml(responseString);
                var nodes = responseXml.GetElementsByTagName(XML_RESPONSE_TAG);
                if (nodes.Count == 0)
                    throw new ApplicationException($"Failed '{methodName}' request. Unexpected response. Server response: {response}");

                var responseNode = nodes[0];
                if (responseNode == null)
                    throw new ApplicationException($"Failed '{methodName}' request. Missing {XML_RESPONSE_TAG} node. Server response: {response}");

                if (responseNode.Attributes[MESSAGE_ATTRIBUTE]?.Value != SUCCESS)
                    throw new ApplicationException($"Failed '{methodName}' request. Non-{SUCCESS}. Description: {responseNode.Attributes["desc"]?.Value}");

                return responseNode;
            }
        }

        private HttpContent GetSyncPostContent(NameValueCollection parameters = null, bool isMultiPart = false)
        {
            var allParameters = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string> ( "uid", _syncUsername),
                new KeyValuePair<string, string> ( "pwd", _syncPassword)
            };

            if (parameters != null)
            {
                foreach (string key in parameters.Keys)
                {
                    allParameters.Add(new KeyValuePair<string, string>(key, parameters[key]));
                }
            }

            if (!isMultiPart)
                return new FormUrlEncodedContent(allParameters);

            var content = new MultipartFormDataContent(Guid.NewGuid().ToString());
            foreach (var parameter in allParameters)
            {
                content.Add(new StringContent(parameter.Value, Encoding.UTF8), parameter.Key);
            }

            return content;
        }
    }
}
