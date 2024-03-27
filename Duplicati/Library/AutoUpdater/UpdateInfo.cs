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

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// The different kinds of releases supported
    /// </summary>
    public enum ReleaseType
    {
        Unknown,
        Stable,
        Beta,
        Experimental,
        Canary,
        Nightly,
        Debug
    }

    public enum UpdateSeverity
    {
        None,
        Low,
        Normal,
        High,
        Critical
    }

    public class UpdateInfo
    {
        public string UpdateFromV1Url;
        public string UpdateFromV2Url;
        public string Displayname;
        public string Version;
        public DateTime ReleaseTime;
        public string ReleaseType;
        public string UpdateSeverity;
        public string ChangeInfo;
        public int PackageUpdaterVersion;
        /// <summary>
        /// Legacy entry, do not use
        /// </summary>
        [Obsolete("Only kept to avoid v2.0.7.x and earlier releases from crashing on nulls")]
        public string[] RemoteURLS = Array.Empty<string>();
        /// <summary>
        /// List of installer packages
        /// </summary>
        public PackageEntry[] Packages;
        /// <summary>
        /// Link to a generic download page
        /// </summary>
        public string GenericUpdatePageUrl;

        /// <summary>
        /// Finds a package that matches the <paramref name="packageTypeId"/>
        /// </summary>
        /// <param name="packageTypeId">The package type id; <c>null</c> uses the currently installed package type id</param>
        /// <returns>The matching package or <c>null</c></returns>
        public PackageEntry? FindPackage(string packageTypeId = null)
        {
            if (!string.IsNullOrWhiteSpace(UpdateFromV2Url) || PackageUpdaterVersion != UpdaterManager.SUPPORTED_PACKAGE_UPDATER_VERSION)
                return null;

            packageTypeId ??= UpdaterManager.PackageTypeId;
            if (string.IsNullOrWhiteSpace(packageTypeId) || Packages == null)
                return null;

            return Packages.FirstOrDefault(x => string.Equals(x.PackageTypeId, packageTypeId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the generic update page url for the <paramref name="packageTypeId"/>
        /// </summary>
        /// <param name="packageTypeId">The package type id; <c>null</c> uses the currently installed package type id</param>
        /// <returns>The generic update page url</returns>
        public string GetGenericUpdatePageUrl(string packageTypeId = null)
        {
            var baseurl = string.IsNullOrWhiteSpace(UpdateFromV2Url)
                ? GenericUpdatePageUrl
                : UpdateFromV2Url;

            packageTypeId ??= UpdaterManager.PackageTypeId;
            if (string.IsNullOrWhiteSpace(packageTypeId))
                return GenericUpdatePageUrl;

            return GenericUpdatePageUrl + $"{(GenericUpdatePageUrl.IndexOf('?') > 0 ? "&" : "?")}packagetypeid={Uri.EscapeDataString(packageTypeId ?? "")}";
        }

        /// <summary>
        /// Gets the updated package urls for the <paramref name="packageTypeId"/>
        /// </summary>
        /// <param name="packageTypeId">The package type id; <c>null</c> uses the currently installed package type id</param>
        /// <returns>The matching update urls</returns>
        public string[] GetUpdateUrls(string packageTypeId = null)
        {
            packageTypeId ??= UpdaterManager.PackageTypeId;
            var package = FindPackage(packageTypeId);
            if (package != null)
                return package.RemoteUrls;

            var generic = GetGenericUpdatePageUrl(packageTypeId);
            if (!string.IsNullOrWhiteSpace(generic))
                return [generic];

            return Array.Empty<string>();
        }

        /// <summary>
        /// Creates a copy of the instance by serializing and deseriaizing the data
        /// </summary>
        /// <returns>A cloned copy</returns>
        public UpdateInfo Clone()
            => System.Text.Json.JsonSerializer.Deserialize<UpdateInfo>(
                System.Text.Json.JsonSerializer.Serialize(this)
            );
    }
}

