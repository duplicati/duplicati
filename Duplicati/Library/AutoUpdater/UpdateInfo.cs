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

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// The different kinds of releases supported
/// </summary>
public enum ReleaseType
{
    /// <summary>
    /// Placeholder for unknown release types
    /// </summary>
    Unknown,
    /// <summary>
    /// A stable release
    /// </summary>
    Stable,
    /// <summary>
    /// A beta release
    /// </summary>
    Beta,
    /// <summary>
    /// An experimental release
    /// </summary>
    Experimental,
    /// <summary>
    /// A canary release
    /// </summary>
    Canary,
    /// <summary>
    /// A nightly release
    /// </summary>
    Nightly,
    /// <summary>
    /// A debug release
    /// </summary>
    Debug
}

/// <summary>
/// The severity of an update
/// </summary>
public enum UpdateSeverity
{
    /// <summary>
    /// No severity specified
    /// </summary>
    None,
    /// <summary>
    /// A low severity update
    /// </summary>
    Low,
    /// <summary>
    /// A normal severity update
    /// </summary>
    Normal,
    /// <summary>
    /// A high severity update
    /// </summary>
    High,
    /// <summary>
    /// A critical severity update
    /// </summary>
    Critical
}

/// <summary>
/// Information about an update package
/// </summary>
/// <param name="MinimumCompatibleVersion">The minimum version required to read this update</param>
/// <param name="IncompatibleUpdateUrl">The URL to present to the user for updating, if this client is too old</param>
/// <param name="Displayname">Name of the update package</param>
/// <param name="Version">Version of the update package</param>
/// <param name="ReleaseTime">The timestamp when the update was released</param>
/// <param name="ReleaseType">The release update type</param>
/// <param name="UpdateSeverity">The severity of the update</param>
/// <param name="ChangeInfo">The changelog text for this entry</param>
/// <param name="PackageUpdaterVersion">The version of the package structure</param>
/// <param name="Packages">List of installer packages</param>
/// <param name="GenericUpdatePageUrl">Link to a generic download page</param>
public record UpdateInfo(
    int MinimumCompatibleVersion,
    string? IncompatibleUpdateUrl,
    string? Displayname,
    string? Version,
    DateTime ReleaseTime,
    string? ReleaseType,
    string? UpdateSeverity,
    string? ChangeInfo,
    int PackageUpdaterVersion,
    PackageEntry[]? Packages,
    string GenericUpdatePageUrl
)
{
    /// <summary>
    /// Finds a package that matches the <paramref name="packageTypeId"/>
    /// </summary>
    /// <param name="packageTypeId">The package type id; <c>null</c> uses the currently installed package type id</param>
    /// <returns>The matching package or <c>null</c></returns>
    public PackageEntry? FindPackage(string? packageTypeId = null)
    {
        if (MinimumCompatibleVersion > UpdaterManager.SUPPORTED_PACKAGE_UPDATER_VERSION)
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
    public string GetGenericUpdatePageUrl(string? packageTypeId = null)
    {
        var baseurl = GenericUpdatePageUrl ?? string.Empty;
        if (MinimumCompatibleVersion > UpdaterManager.SUPPORTED_PACKAGE_UPDATER_VERSION && !string.IsNullOrWhiteSpace(IncompatibleUpdateUrl))
            baseurl = IncompatibleUpdateUrl;

        packageTypeId ??= UpdaterManager.PackageTypeId;
        if (string.IsNullOrWhiteSpace(packageTypeId))
            return baseurl;

        return baseurl + $"{(baseurl.IndexOf('?') > 0 ? "&" : "?")}packagetypeid={Uri.EscapeDataString(packageTypeId ?? "")}";
    }

    /// <summary>
    /// Gets the updated package urls for the <paramref name="packageTypeId"/>
    /// </summary>
    /// <param name="packageTypeId">The package type id; <c>null</c> uses the currently installed package type id</param>
    /// <returns>The matching update urls</returns>
    public string[] GetUpdateUrls(string? packageTypeId = null)
    {
        packageTypeId ??= UpdaterManager.PackageTypeId;
        var package = FindPackage(packageTypeId);
        if (package != null)
            return package.RemoteUrls;

        var generic = GetGenericUpdatePageUrl(packageTypeId);
        if (!string.IsNullOrWhiteSpace(generic))
            return [generic];

        return [];
    }
}


