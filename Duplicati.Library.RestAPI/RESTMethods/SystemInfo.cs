// Copyright (C) 2024, The Duplicati Team
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
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Common;
using Duplicati.Library.RestAPI;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class SystemInfo : IRESTMethodGET, IRESTMethodDocumented
    {
        public string Description { get { return "Gets various system properties"; } }
        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, SystemData(null).GetType())
                };
            }
        }

        public void GET(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.BodyWriter.OutputOK(SystemData(info));
            }
            else if (key.Equals("filtergroups", StringComparison.OrdinalIgnoreCase))
            {
                info.BodyWriter.OutputOK(FilterGroups());
            }
            else
            {
                info.OutputError(code: System.Net.HttpStatusCode.NotFound, reason: "Not found");
            }
        }

        private static object SystemData(RequestInfo info)
        {
            var browserlanguage = RESTHandler.ParseDefaultRequestCulture(info) ?? System.Globalization.CultureInfo.InvariantCulture;

            return new
            {
                APIVersion = 1,
                PasswordPlaceholder = Duplicati.Server.WebServer.Server.PASSWORD_PLACEHOLDER,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                ServerVersionName = Duplicati.License.VersionNumbers.Version,
                ServerVersionType = Duplicati.Library.AutoUpdater.UpdaterManager.SelfVersion.ReleaseType,
                StartedBy = FIXMEGlobal.Origin,
                DefaultUpdateChannel = Duplicati.Library.AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel,
                DefaultUsageReportLevel = Duplicati.Library.UsageReporter.Reporter.DefaultReportLevel,
                ServerTime = DateTime.Now,
                OSType = Platform.IsClientPosix ? (Platform.IsClientOSX ? "OSX" : "Linux") : "Windows",
                DirectorySeparator = System.IO.Path.DirectorySeparatorChar,
                PathSeparator = System.IO.Path.PathSeparator,
                CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
                MachineName = System.Environment.MachineName,
                PackageTypeId = Duplicati.Library.AutoUpdater.UpdaterManager.PackageTypeId,
                UserName = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().Name : System.Environment.UserName,
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
                ServerModules = Serializable.ServerSettings.ServerModules,
                UsingAlternateUpdateURLs = Duplicati.Library.AutoUpdater.AutoUpdateSettings.UsesAlternateURLs,
                LogLevels = Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)),
                SpecialFolders = from n in SpecialFolders.Nodes select new { ID = n.id, Path = n.resolvedpath },
                BrowserLocale = new
                {
                    Code = browserlanguage.Name,
                    EnglishName = browserlanguage.EnglishName,
                    DisplayName = browserlanguage.NativeName
                },
                SupportedLocales =
                    Library.Localization.LocalizationService.SupportedCultures
                            .Select(x => new
                            {
                                Code = x,
                                EnglishName = new System.Globalization.CultureInfo(x).EnglishName,
                                DisplayName = new System.Globalization.CultureInfo(x).NativeName
                            }
                            ),
                BrowserLocaleSupported = Library.Localization.LocalizationService.isCultureSupported(browserlanguage)
            };
        }

        private static object FilterGroups()
        {
            return new { FilterGroups = Library.Utility.FilterGroups.GetFilterStringMap() };
        }
    }
}

