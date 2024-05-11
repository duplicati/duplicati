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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Duplicati.Library.AutoUpdater
{
    public static class AutoUpdateSettings
    {
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private const string APP_NAME = "AutoUpdateAppName.txt";
        private const string UPDATE_URL = "AutoUpdateURL.txt";
        private const string UPDATE_KEY = "AutoUpdateSignKeys.txt";
        private const string UPDATE_CHANNEL = "AutoUpdateBuildChannel.txt";
        private const string UPDATE_INSTALL_FILE = "AutoUpdateInstallIDTemplate.txt";
        private const string UPDATE_MACHINE_FILE = "AutoUpdateMachineIDTemplate.txt";

        private const string OEM_APP_NAME = "oem-app-name.txt";
        private const string OEM_UPDATE_URL = "oem-update-url.txt";
        private const string OEM_UPDATE_KEY = "oem-update-key.txt";
        private const string OEM_UPDATE_INSTALL_FILE = "oem-update-installid.txt";

        public const string UPDATEURL_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_URLS";
        public const string UPDATECHANNEL_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_CHANNEL";

        internal const string MATCH_UPDATE_URL_PREFIX_GROUP = "prefix";
        internal const string MATCH_UPDATE_URL_CHANNEL_GROUP = "channel";
        internal const string MATCH_UPDATE_URL_FILENAME_GROUP = "filename";

        internal static readonly Regex MATCH_AUTOUPDATE_URL =
            new Regex(string.Format(
                "(?<{0}>.+)(?<{1}>{3})(?<{2}>/([^/]+).manifest)",
                MATCH_UPDATE_URL_PREFIX_GROUP,
                MATCH_UPDATE_URL_CHANNEL_GROUP,
                MATCH_UPDATE_URL_FILENAME_GROUP,
                string.Join("|", Enum.GetNames(typeof(ReleaseType)).Union(new[] { "preview" }))), RegexOptions.Compiled | RegexOptions.IgnoreCase);


        static AutoUpdateSettings()
        {
            ReadResourceText(APP_NAME, OEM_APP_NAME);
            ReadResourceText(UPDATE_URL, OEM_UPDATE_URL);
            ReadResourceText(UPDATE_KEY, OEM_UPDATE_KEY);
            ReadResourceText(UPDATE_INSTALL_FILE, OEM_UPDATE_INSTALL_FILE);
            ReadResourceText(UPDATE_MACHINE_FILE, null);
            ReadResourceText(UPDATE_CHANNEL, null);
        }

        private static string ReadResourceText(string name, string oemname)
        {
            // First try to read from _cache
            if (_cache.TryGetValue(name, out string result))
                return result;

            try
            {
                using (var rd = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(AutoUpdateSettings), name)))
                    result = rd.ReadToEnd();
            }
            catch
            {
            }

            try
            {
                // Check for OEM override
                if (!string.IsNullOrWhiteSpace(oemname) && System.IO.File.Exists(oemname))
                    result = System.IO.File.ReadAllText(oemname);
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(result))
                result = "";
            else
                result = result.Trim();

            _cache[name] = result;
            return result;
        }

        public static string[] URLs
        {
            get
            {
                if (UsesAlternateURLs)
                    return Environment.GetEnvironmentVariable(string.Format(UPDATEURL_ENVNAME_TEMPLATE, AppName)).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                else
                    return ReadResourceText(UPDATE_URL, OEM_UPDATE_URL).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public static bool UsesAlternateURLs
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(string.Format(UPDATEURL_ENVNAME_TEMPLATE, AppName)));
            }
        }

        public static ReleaseType DefaultUpdateChannel
        {
            get
            {
                var channelstring = Environment.GetEnvironmentVariable(string.Format(UPDATECHANNEL_ENVNAME_TEMPLATE, AppName));

                if (UsesAlternateURLs && string.IsNullOrWhiteSpace(channelstring))
                {
                    foreach (var url in URLs)
                    {
                        var match = AutoUpdateSettings.MATCH_AUTOUPDATE_URL.Match(url);
                        if (match.Success)
                        {
                            channelstring = match.Groups[AutoUpdateSettings.MATCH_UPDATE_URL_CHANNEL_GROUP].Value;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(channelstring))
                    channelstring = BuildUpdateChannel;

                // Update from older builds
                if (string.Equals(channelstring, "preview", StringComparison.OrdinalIgnoreCase))
                    channelstring = ReleaseType.Experimental.ToString();
                if (string.Equals(channelstring, "rene", StringComparison.OrdinalIgnoreCase))
                    channelstring = ReleaseType.Canary.ToString();

                ReleaseType rt;
                if (!Enum.TryParse<ReleaseType>(channelstring, true, out rt))
                    rt = ReleaseType.Stable;

                return rt;
            }
        }

        public static string AppName
        {
            get { return ReadResourceText(APP_NAME, OEM_APP_NAME); }
        }

        public static string BuildUpdateChannel
        {
            get { return ReadResourceText(UPDATE_CHANNEL, null); }
        }

        public static string UpdateInstallFileText
        {
            get { return string.Format(ReadResourceText(UPDATE_INSTALL_FILE, OEM_UPDATE_INSTALL_FILE), Guid.NewGuid().ToString("N")); }
        }

        public static string UpdateMachineFileText(string machineid)
            => string.Format(ReadResourceText(UPDATE_MACHINE_FILE, "{0}"), string.IsNullOrWhiteSpace(machineid) ? Guid.NewGuid().ToString("N") : machineid);

        public static RSACryptoServiceProvider[] SignKeys
        {
            get
            {
                var keys = new List<RSACryptoServiceProvider>();

                try
                {
                    var src = ReadResourceText(UPDATE_KEY, OEM_UPDATE_KEY);

                    // Allow multiple keys, one per line
                    // For fallback, read the whole string as a key, in case there are old ones with line breaks

                    var keystrings = src.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Prepend(src.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct();

                    foreach (var str in keystrings)
                    {
                        try
                        {
                            var key = new RSACryptoServiceProvider();
                            key.FromXmlString(str);
                            keys.Add(key);
                        }
                        catch
                        { }
                    }
                }
                catch
                {
                }

                return keys.ToArray();
            }
        }
    }
}

