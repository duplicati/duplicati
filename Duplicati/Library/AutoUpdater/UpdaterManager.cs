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
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Utility;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using System.Threading;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// Handles operations related to updating the application
    /// </summary>
    public static class UpdaterManager
    {
        /// <summary>
        /// The RSA key used to sign the manifest
        /// </summary>
        private static System.Security.Cryptography.RSA[] SIGN_KEYS => AutoUpdateSettings.SignKeys;
        /// <summary>
        /// Urls to check for updated packages
        /// </summary>
        private static string[] MANIFEST_URLS => AutoUpdateSettings.URLs;
        /// <summary>
        /// The app name to show
        /// </summary>
        private static string APPNAME => AutoUpdateSettings.AppName;
        /// <summary>
        /// The version that the updater supports
        /// </summary>
        public const int SUPPORTED_PACKAGE_UPDATER_VERSION = 2;
        /// <summary>
        /// The directory where the program is running from
        /// </summary>
        public static readonly string INSTALLATIONDIR;
        /// <summary>
        /// Env variable that allows fully disabling all update checks
        /// </summary>
        public static readonly bool DISABLE_UPDATE_CHECK = Debugger.IsAttached || Utility.Utility.ParseBool(Environment.GetEnvironmentVariable(string.Format(SKIPUPDATE_ENVNAME_TEMPLATE, APPNAME)), false);
        /// <summary>
        /// The operating system display name
        /// </summary>
        public static readonly string OperatingSystemName = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "MacOS" : "Unknown";
        /// <summary>
        /// The update information for the running version
        /// </summary>
        public static readonly UpdateInfo SelfVersion;

        /// <summary>
        /// Event trigger for errors on update
        /// </summary>
        public static event Action<Exception>? OnError;

        /// <summary>
        /// Common formatting string for date-time values
        /// </summary>
        private const string DATETIME_FORMAT = "yyyymmddhhMMss";
        /// <summary>
        /// The template for the environment variable that toggles disabling updates
        /// </summary>
        public const string SKIPUPDATE_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_SKIP_UPDATE";
        /// <summary>
        /// The name of the file that contains the manifest, located in the <see cref="INSTALLATIONDIR"/> folder
        /// </summary>
        private const string UPDATE_MANIFEST_FILENAME = "autoupdate.manifest";
        /// <summary>
        /// The name of the file that contains the package type id, located in the <see cref="INSTALLATIONDIR"/> folder
        /// </summary>
        private const string PACKAGE_TYPE_FILE = "package_type_id.txt";

        /// <summary>
        /// Gets the last version found from an update
        /// </summary>
        public static UpdateInfo? LastUpdateCheckVersion { get; private set; }

        /// <summary>
        /// The default timeout in seconds for download operations
        /// </summary>
        private const int DOWNLOAD_OPERATION_TIMEOUT_SECONDS = 3600;

        /// <summary>
        /// The default timeout in seconds for fast get version metadata operations
        /// </summary>
        private const int SHORT_OPERATION_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// Performs static initialization of the update manager, populating the readonly fields of the manager
        /// </summary>
        static UpdaterManager()
        {
            // Set the installation path
            INSTALLATIONDIR = Util.AppendDirSeparator(Path.GetDirectoryName(Utility.Utility.getEntryAssembly().Location));

            // Attempt to read the installed manifest file
            UpdateInfo? selfVersion = null;
            try
            {
                selfVersion = ReadInstalledManifest(INSTALLATIONDIR);
            }
            catch
            {
            }

            // In case the installed manifest is broken, try to set some sane values
            SelfVersion = selfVersion ?? new UpdateInfo(
                MinimumCompatibleVersion: 1,
                PackageUpdaterVersion: 1,
                IncompatibleUpdateUrl: string.Empty,
                GenericUpdatePageUrl: "https://duplicati.com/download",
                UpdateSeverity: null,
                ChangeInfo: null,
                Packages: null,
                Displayname: string.IsNullOrWhiteSpace(License.VersionNumbers.TAG) ? "Current" : License.VersionNumbers.TAG,
                Version: System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ReleaseTime: new DateTime(0),
                ReleaseType:
#if DEBUG
                    "Debug"
#else
                        string.IsNullOrWhiteSpace(AutoUpdateSettings.BuildUpdateChannel) ? "Nightly" : AutoUpdateSettings.BuildUpdateChannel
#endif
            );
        }

        public static Version TryParseVersion(string? str)
        {
            if (Version.TryParse(str, out var v))
                return v;
            else
                return new Version(0, 0);
        }


        /// <summary>
        /// The package type ID
        /// </summary>
        public static string PackageTypeId => _packageTypeId.Value;

        /// <summary>
        /// The package type ID, lazy evaluated
        /// </summary>
        private static readonly Lazy<string> _packageTypeId = new(() =>
        {
            try { return File.ReadAllLines(Path.Combine(INSTALLATIONDIR!, PACKAGE_TYPE_FILE)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? ""; }
            catch { }

#if DEBUG
            return "debug";
#else
            return "";
#endif
        });


        /// <summary>
        /// Checks for updates and returns the update information if available
        /// </summary>
        /// <param name="channel">The release channel to check for updates</param>
        /// <returns>The update information if available, or null if no updates are available</returns>
        public static UpdateInfo? CheckForUpdate(ReleaseType channel = ReleaseType.Unknown)
        {
            if (channel == ReleaseType.Unknown)
                channel = AutoUpdateSettings.DefaultUpdateChannel;

            foreach (var rawurl in MANIFEST_URLS)
            {
                var url = rawurl;

                // Attempt to match the url to change the channel if possible
                // This allows overrides to the URLs for deployment of custom builds,
                // but does not require that they adopt the channel system
                var match = AutoUpdateSettings.MATCH_AUTOUPDATE_URL.Match(url);
                if (match.Success)
                {
                    var mg = match.Groups[AutoUpdateSettings.MATCH_UPDATE_URL_CHANNEL_GROUP];

                    // Replace the channel name with the chosen channel
                    url =
                        url.Substring(0, mg.Index)
                        +
                        channel.ToString().ToLowerInvariant()
                        +
                        url.Substring(mg.Index + mg.Length);
                }

                try
                {
                    if (SIGN_KEYS.Length == 0)
                        throw new Exception("No signing keys are available, cannot check update");

                    using (var tmpfile = new TempFile())
                    {

                        using var request = new HttpRequestMessage(HttpMethod.Get, url);

                        request.Headers.Add(System.Net.HttpRequestHeader.UserAgent.ToString(), string.Format("{0} v{1}{2}", APPNAME, SelfVersion.Version, string.IsNullOrWhiteSpace(DataFolderManager.InstallID) ? "" : " -" + DataFolderManager.InstallID));
                        request.Headers.Add("X-Install-ID", DataFolderManager.InstallID);
                        request.Headers.Add("X-Package-Type-ID", PackageTypeId);

                        using var timeoutToken = new CancellationTokenSource();
                        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
                        using (var client = HttpClientHelper.CreateClient())
                            client.DownloadFile(request, tmpfile, null, timeoutToken.Token).Await();

                        using (var fs = File.OpenRead(tmpfile))
                        {
                            var verifyOps = SIGN_KEYS.Select(k => new JSONSignature.VerifyOperation(
                                Algorithm: JSONSignature.RSA_SHA256,
                                PublicKey: k.ToXmlString(false)
                            ));

                            if (!JSONSignature.VerifyAtLeastOne(fs, verifyOps))
                                throw new Exception("No valid signature found in manifest file");

                            var update = JsonSerializer.Deserialize<UpdateInfo>(fs);
                            if (update == null)
                                return null;

                            if (TryParseVersion(update.Version) <= TryParseVersion(SelfVersion.Version))
                                return null;

                            // Don't install a debug update on a release build and vice versa
                            if (string.Equals(SelfVersion.ReleaseType, "Debug", StringComparison.OrdinalIgnoreCase) && !string.Equals(update.ReleaseType, SelfVersion.ReleaseType, StringComparison.OrdinalIgnoreCase))
                                return null;

                            ReleaseType rt;
                            if (!Enum.TryParse<ReleaseType>(update.ReleaseType, true, out rt))
                                rt = ReleaseType.Unknown;

                            // If the update is too low to be considered, skip it
                            // Should never happen, but protects against mistakes in deployment
                            if (rt > channel)
                                return null;

                            // In case the manifest does not contain a URL, use the one from this assembly
                            if (string.IsNullOrWhiteSpace(update.GenericUpdatePageUrl))
                                update = update with { GenericUpdatePageUrl = SelfVersion.GenericUpdatePageUrl };

                            // In case there is no url, fall back to the project download page
                            if (string.IsNullOrWhiteSpace(update.GenericUpdatePageUrl))
                                update = update with { GenericUpdatePageUrl = "https://duplicati.com/download" };

                            LastUpdateCheckVersion = update;
                            return update;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (OnError != null)
                        OnError(ex);
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the installed manifest file
        /// </summary>
        /// <param name="folder">The folder to read the manifest from</param>
        /// <returns>The manifest if found, or null if not found</returns>
        private static UpdateInfo? ReadInstalledManifest(string folder)
        {
            var manifest = Path.Combine(folder, UPDATE_MANIFEST_FILENAME);
            if (File.Exists(manifest))
            {
                try
                {
                    var verifyOps = SIGN_KEYS.Select(k => new JSONSignature.VerifyOperation(
                        Algorithm: JSONSignature.RSA_SHA256,
                        PublicKey: k.ToXmlString(false)
                    ));

                    using (var fs = File.OpenRead(manifest))
                    {
                        if (!JSONSignature.VerifyAtLeastOne(fs, verifyOps))
                            throw new Exception("Installed manifest signature is invalid");

                        return JsonSerializer.Deserialize<UpdateInfo>(fs);
                    }
                }
                catch (Exception ex)
                {
                    if (OnError != null)
                        OnError(ex);
                }
            }

            return null;
        }

        /// <summary>
        /// Downloads the update package
        /// </summary>
        /// <param name="version">The version to download</param>
        /// <param name="package">The package to download</param>
        /// <param name="targetPath">The path to save the downloaded package to</param>
        /// <param name="progress">The progress callback</param>
        /// <returns>True if the download was successful, otherwise false</returns>
        public static bool DownloadUpdate(UpdateInfo version, PackageEntry package, string targetPath, Action<double>? progress = null)
        {
            var updates = package.RemoteUrls.ToList();

            // If alternate update URLs are specified,
            // we look for packages there as well
            if (AutoUpdateSettings.UsesAlternateURLs)
            {
                var packagepath = new Library.Utility.Uri(updates[0]).Path;
                var packagename = packagepath.Split('/').Last();

                foreach (var alt_url in AutoUpdateSettings.URLs.Reverse())
                {
                    var alt_uri = new Library.Utility.Uri(alt_url);
                    var path_components = alt_uri.Path.Split('/');
                    var path = string.Join("/", path_components.Take(path_components.Count() - 1).Union(new string[] { packagename }));

                    var new_path = alt_uri.SetPath(path);
                    updates.Insert(0, new_path.ToString());
                }
            }

            using (var tempfilename = new Library.Utility.TempFile())
            {
                foreach (var url in updates)
                {
                    try
                    {
                        using (var tempfile = File.Open(tempfilename, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                        {
                            Action<long>? cb = null;
                            if (progress != null)
                                cb = (s) => { progress(Math.Min(1.0, Math.Max(0.0, (double)s / package.Length))); };

                            using var request = new HttpRequestMessage(HttpMethod.Get, url);

                            request.Headers.Add(System.Net.HttpRequestHeader.UserAgent.ToString(), string.Format("{0} v{1}", APPNAME, SelfVersion.Version));
                            request.Headers.Add("X-Install-ID", DataFolderManager.InstallID);

                            using var timeoutToken = new CancellationTokenSource();
                            timeoutToken.CancelAfter(TimeSpan.FromSeconds(DOWNLOAD_OPERATION_TIMEOUT_SECONDS));

                            using (var client = HttpClientHelper.CreateClient())
                                client.DownloadFile(request, tempfile, cb, timeoutToken.Token).Await();

                            var sha256 = System.Security.Cryptography.SHA256.Create();
                            var md5 = System.Security.Cryptography.MD5.Create();

                            if (tempfile.Length != package.Length)
                                throw new Exception(string.Format("Invalid file size {0}, expected {1} for {2}", tempfile.Length, package.Length, url));

                            tempfile.Position = 0;
                            var sha256hash = Convert.ToBase64String(sha256.ComputeHash(tempfile));
                            if (sha256hash != package.SHA256)
                                throw new Exception(string.Format("Damaged or corrupted file, sha256 mismatch for {0}", url));


                            tempfile.Position = 0;
                            var md5hash = Convert.ToBase64String(md5.ComputeHash(tempfile));
                            if (md5hash != package.MD5)
                                throw new Exception(string.Format("Damaged or corrupted file, md5 mismatch for {0}", url));
                        }

                        File.Copy(tempfilename, targetPath, true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        if (OnError != null)
                            OnError(ex);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Helper method to create a signed manifest file
        /// </summary>
        /// <param name="key">The key used for signing the manifest</param>
        /// <param name="sourcedata">The template content in JSON format</param>
        /// <param name="outputfolder">The folder where the signed manifest will be written to</param>
        /// <param name="version">The version of the manifest</param>
        /// <param name="incompatibleUpdateUrl">The URL to use for incompatible updates</param>
        /// <param name="genericUpdatePageUrl">The URL to use for generic updates</param>
        public static void CreateSignedManifest(IEnumerable<System.Security.Cryptography.RSA> keys, string sourcedata, string outputfolder, string? version = null, string? incompatibleUpdateUrl = null, string? genericUpdatePageUrl = null, string? releaseType = null, IEnumerable<PackageEntry>? packages = null)
        {
            // Read the existing manifest
            var remoteManifest = JsonSerializer.Deserialize<UpdateInfo>(string.IsNullOrWhiteSpace(sourcedata) ? "{}" : sourcedata)
                ?? throw new Exception("Failed to deserialize the manifest from source data");

            if (remoteManifest.ReleaseTime.Ticks == 0)
                remoteManifest = remoteManifest with { ReleaseTime = DateTime.UtcNow };

            // No files to update with are allowed, as we currently do not use the information
            if (remoteManifest.Packages == null)
                remoteManifest = remoteManifest with { Packages = Array.Empty<PackageEntry>() };

            remoteManifest = remoteManifest with
            {
                PackageUpdaterVersion = SUPPORTED_PACKAGE_UPDATER_VERSION,
                MinimumCompatibleVersion = 2
            };

            if (version != null)
                remoteManifest = remoteManifest with { Version = version.ToString() };
            if (!string.IsNullOrWhiteSpace(incompatibleUpdateUrl))
                remoteManifest = remoteManifest with { IncompatibleUpdateUrl = incompatibleUpdateUrl };
            if (!string.IsNullOrWhiteSpace(genericUpdatePageUrl))
                remoteManifest = remoteManifest with { GenericUpdatePageUrl = genericUpdatePageUrl };
            if (!string.IsNullOrWhiteSpace(releaseType))
                remoteManifest = remoteManifest with { ReleaseType = releaseType };
            if (packages != null)
                remoteManifest = remoteManifest with { Packages = packages.ToArray() };

            if (string.IsNullOrWhiteSpace(remoteManifest.IncompatibleUpdateUrl))
                remoteManifest = remoteManifest with { IncompatibleUpdateUrl = remoteManifest.GenericUpdatePageUrl };

            if (string.IsNullOrWhiteSpace(remoteManifest.IncompatibleUpdateUrl))
                throw new Exception($"Field must be set: {nameof(remoteManifest.IncompatibleUpdateUrl)}");
            if (string.IsNullOrWhiteSpace(remoteManifest.GenericUpdatePageUrl))
                throw new Exception($"Field must be set: {nameof(remoteManifest.GenericUpdatePageUrl)}");
            if (string.IsNullOrWhiteSpace(remoteManifest.Version))
                throw new Exception($"Field must be set: {nameof(remoteManifest.Version)}");
            if (string.IsNullOrWhiteSpace(remoteManifest.ReleaseType))
                throw new Exception($"Field must be set: {nameof(remoteManifest.ReleaseType)}");

            // Write a signed manifest for upload
            using (var tf = new TempFile())
            {
                using (var ms = new MemoryStream())
                {
                    JsonSerializer.Serialize<UpdateInfo>(ms, remoteManifest, new JsonSerializerOptions { WriteIndented = false });
                    ms.Position = 0;

                    var signops = keys.Select(k => new JSONSignature.SignOperation(
                        Algorithm: JSONSignature.RSA_SHA256,
                        PublicKey: k.ToXmlString(false),
                        PrivateKey: k.ToXmlString(true)
                    ));

                    using (var fs = File.Create(tf))
                        JSONSignature.SignAsync(ms, fs, signops).ConfigureAwait(false).GetAwaiter().GetResult();
                }

                // Validate that the written file can also be read  
                using (var fs = File.OpenRead(tf))
                {
                    var validSigs = JSONSignature.Verify(fs, keys.Select(k => new JSONSignature.VerifyOperation(
                        Algorithm: JSONSignature.RSA_SHA256,
                        PublicKey: k.ToXmlString(false)
                    )));

                    if (validSigs.Count() != keys.Count())
                        throw new Exception("Failed to verify all signatures after signing");

                    var deserialized = JsonSerializer.Deserialize<UpdateInfo>(fs);
                    if (deserialized == null || deserialized.Version != remoteManifest.Version)
                        throw new Exception("Failed to deserialize the signed manifest");
                }

                File.Move(tf, Path.Combine(outputfolder, UPDATE_MANIFEST_FILENAME));
            }
        }
    }
}

