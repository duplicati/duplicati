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
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class SystemInfo : IRESTMethodGET, IRESTMethodPOST, IRESTMethodDocumented
    {
        public string Description { get { return "Gets various system properties"; } }
        public IEnumerable<KeyValuePair<string, Type>> Types
        { 
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, SystemData.GetType())
                };
            }
        }

        public void GET(string key, RequestInfo info)
        {
            info.BodyWriter.OutputOK(SystemData);            
        }

        public void POST(string key, RequestInfo info)
        {
            var input = info.Request.Form;
            switch ((key ?? "").ToLowerInvariant())
            {
                case "suppressdonationmessages":
                    Library.Main.Utility.SuppressDonationMessages = true;
                    info.OutputOK();
                    return;

                case "showdonationmessages":
                    Library.Main.Utility.SuppressDonationMessages = false;
                    info.OutputOK();
                    return;

                default:
                    info.ReportClientError("No such action", System.Net.HttpStatusCode.NotFound);
                    return;
            }
        }

        private static object SystemData
        {
            get
            {
                return new
                {
                    APIVersion = 1,
                    PasswordPlaceholder = Duplicati.Server.WebServer.Server.PASSWORD_PLACEHOLDER,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    ServerVersionName = Duplicati.License.VersionNumbers.Version,
                    ServerVersionType = Duplicati.Library.AutoUpdater.UpdaterManager.SelfVersion.ReleaseType,
                    BaseVersionName = Duplicati.Library.AutoUpdater.UpdaterManager.BaseVersion.Displayname,
                    DefaultUpdateChannel = Duplicati.Library.AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel,
                    DefaultUsageReportLevel = Duplicati.Library.UsageReporter.Reporter.DefaultReportLevel,
                    ServerTime = DateTime.Now,
                    OSType = Library.Utility.Utility.IsClientLinux ? (Library.Utility.Utility.IsClientOSX ? "OSX" : "Linux") : "Windows",
                    DirectorySeparator = System.IO.Path.DirectorySeparatorChar,
                    PathSeparator = System.IO.Path.PathSeparator,
                    CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
                    MonoVersion = Duplicati.Library.Utility.Utility.IsMono ? Duplicati.Library.Utility.Utility.MonoVersion.ToString() : null,
                    MachineName = System.Environment.MachineName,
                    NewLine = System.Environment.NewLine,
                    CLRVersion = System.Environment.Version.ToString(),
                    CLROSInfo = new
                    {
                        Platform = System.Environment.OSVersion.Platform.ToString(),
                        ServicePack = System.Environment.OSVersion.ServicePack,
                        Version = System.Environment.OSVersion.Version.ToString(),
                        VersionString = System.Environment.OSVersion.VersionString
                    },
                    Options = Serializable.ServerSettings.Options,
                    CompressionModules = Serializable.ServerSettings.CompressionModules,
                    EncryptionModules = Serializable.ServerSettings.EncryptionModules,
                    BackendModules = Serializable.ServerSettings.BackendModules,
                    GenericModules = Serializable.ServerSettings.GenericModules,
                    WebModules = Serializable.ServerSettings.WebModules,
                    ConnectionModules = Serializable.ServerSettings.ConnectionModules,
                    UsingAlternateUpdateURLs = Duplicati.Library.AutoUpdater.AutoUpdateSettings.UsesAlternateURLs,
                    LogLevels = Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)),
                    SuppressDonationMessages = Duplicati.Library.Main.Utility.SuppressDonationMessages
                };
            }
        }
    }
}

