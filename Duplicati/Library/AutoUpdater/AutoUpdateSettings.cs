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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// Contains settings for the auto-updater
    /// </summary>
    public static class AutoUpdateSettings
    {
        /// <summary>
        /// The appname file resource name
        /// </summary>
        private const string APP_NAME = "AutoUpdateAppName.txt";
        /// <summary>
        /// The update URL file resource name
        /// </summary>
        private const string UPDATE_URL = "AutoUpdateURL.txt";
        /// <summary>
        /// The update key file resource name
        /// </summary>
        private const string UPDATE_KEY = "AutoUpdateSignKeys.txt";
        /// <summary>
        /// The update channel file resource name
        /// </summary>
        private const string UPDATE_CHANNEL = "AutoUpdateBuildChannel.txt";
        /// <summary>
        /// The update install file template resource name
        /// </summary>
        private const string UPDATE_INSTALL_FILE = "AutoUpdateInstallIDTemplate.txt";
        /// <summary>
        /// The update machine file template resource name
        /// </summary>
        private const string UPDATE_MACHINE_FILE = "AutoUpdateMachineIDTemplate.txt";

        /// <summary>
        /// The OEM file name
        /// </summary>
        private const string OEM_APP_NAME = "oem-app-name.txt";
        /// <summary>
        /// The OEM update URL file name
        /// </summary>
        private const string OEM_UPDATE_URL = "oem-update-url.txt";
        /// <summary>
        /// The OEM update key file name
        /// </summary>
        private const string OEM_UPDATE_KEY = "oem-update-key.txt";
        /// <summary>
        /// The OEM update install template file name
        /// </summary>
        private const string OEM_UPDATE_INSTALL_FILE = "oem-update-installid.txt";

        /// <summary>
        /// The update URL environment variable name template
        /// </summary>
        public const string UPDATEURL_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_URLS";
        /// <summary>
        /// The update channel environment variable name template
        /// </summary>
        public const string UPDATECHANNEL_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_CHANNEL";

        /// <summary>
        /// The package prefix group name in the <see cref="MATCH_AUTOUPDATE_URL"/> regex
        /// </summary>
        internal const string MATCH_UPDATE_URL_PREFIX_GROUP = "prefix";
        /// <summary>
        /// The channel group name in the <see cref="MATCH_AUTOUPDATE_URL"/> regex
        /// </summary>
        internal const string MATCH_UPDATE_URL_CHANNEL_GROUP = "channel";
        /// <summary>
        /// The filename group name in the <see cref="MATCH_AUTOUPDATE_URL"/> regex
        /// </summary>
        internal const string MATCH_UPDATE_URL_FILENAME_GROUP = "filename";

        /// <summary>
        /// The regex to match the auto-update URL and replace the channel
        /// </summary>
        internal static readonly Regex MATCH_AUTOUPDATE_URL =
            new Regex(string.Format(
                "(?<{0}>.+)(?<{1}>{3})(?<{2}>/([^/]+).manifest)",
                MATCH_UPDATE_URL_PREFIX_GROUP,
                MATCH_UPDATE_URL_CHANNEL_GROUP,
                MATCH_UPDATE_URL_FILENAME_GROUP,
                string.Join("|", Enum.GetNames(typeof(ReleaseType)).Union(new[] { "preview" }))), RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads a resource text from the assembly, with an optional OEM filename override
        /// </summary>
        /// <param name="name">The embedded resource name</param>
        /// <param name="oemname">The OEM filename override</param>
        /// <returns>The text of the resource</returns>
        private static string ReadResourceText(string name, string? oemname)
        {
            string? result = null;
            try
            {
                using (var rs = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(AutoUpdateSettings), name))
                    if (rs != null)
                        using (var rd = new System.IO.StreamReader(rs))
                            result = rd.ReadToEnd();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(nameof(AutoUpdateSettings), "ReadResourceStreamError", ex, "Failed to read resource {0}: {1}", name, ex.Message);
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

            return result;
        }

        /// <summary>
        /// The URLs to check for updates
        /// </summary>
        public static string[] URLs => _URLs.Value;

        /// <summary>
        /// The alternate URLs to check for updates, lazy evaluated
        /// </summary>
        private static readonly Lazy<string?> _alternateURLs = new Lazy<string?>(() =>
        {
            return Environment.GetEnvironmentVariable(string.Format(UPDATEURL_ENVNAME_TEMPLATE, AppName));
        });

        /// <summary>
        /// The URLs to check for updates, lazy evaluated
        /// </summary>
        private static readonly Lazy<string[]> _URLs = new Lazy<string[]>(() =>
        {
            if (!string.IsNullOrWhiteSpace(_alternateURLs.Value))
                return _alternateURLs.Value.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            else
                return ReadResourceText(UPDATE_URL, OEM_UPDATE_URL).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        });

        /// <summary>
        /// Indicates if alternate URLs are in use
        /// </summary>
        public static bool UsesAlternateURLs => !string.IsNullOrWhiteSpace(_alternateURLs.Value);

        /// <summary>
        /// The default update channel
        /// </summary>
        public static ReleaseType DefaultUpdateChannel => _defaultUpdateChannel.Value;

        /// <summary>
        /// The default update channel, lazy evaluated
        /// </summary>
        private static readonly Lazy<ReleaseType> _defaultUpdateChannel = new Lazy<ReleaseType>(() =>
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
        });

        /// <summary>
        /// The application name
        /// </summary>
        public static string AppName => _appName.Value;

        /// <summary>
        /// The application name, lazy evaluated
        /// </summary>
        private static readonly Lazy<string> _appName = new Lazy<string>(() => ReadResourceText(APP_NAME, OEM_APP_NAME));

        /// <summary>
        /// The build update channel
        /// </summary>
        public static string BuildUpdateChannel => _buildUpdateChannel.Value;

        /// <summary>
        /// The build update channel, lazy evaluated
        /// </summary>
        private static readonly Lazy<string> _buildUpdateChannel = new Lazy<string>(() => ReadResourceText(UPDATE_CHANNEL, null));

        /// <summary>
        /// The text for the update install file
        /// </summary>
        public static string UpdateInstallFileText => string.Format(ReadResourceText(UPDATE_INSTALL_FILE, OEM_UPDATE_INSTALL_FILE), Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Updates the machine file text with the machine ID
        /// </summary>
        /// <param name="machineid">The machine ID to use</param>
        /// <returns>The updated text</returns>
        public static string UpdateMachineFileText(string machineid)
            => string.Format(ReadResourceText(UPDATE_MACHINE_FILE, "{0}"), string.IsNullOrWhiteSpace(machineid) ? Guid.NewGuid().ToString("N") : machineid);

        /// <summary>
        /// The keys to use for signing
        /// </summary>
        public static RSACryptoServiceProvider[] SignKeys => _signKeys.Value;

        /// <summary>
        /// The keys to use for signing, lazy evaluated
        /// </summary>
        private static readonly Lazy<RSACryptoServiceProvider[]> _signKeys = new Lazy<RSACryptoServiceProvider[]>(() =>
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
        });
    }
}

