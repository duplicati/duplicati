// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Utility;
using Duplicati.Proprietary.LicenseChecker;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Configuration support for proprietary modules
/// </summary>
public static class Configuration
{
    public static string[] LicensedAPIExtensions => LicensedAPIExtensionsLazy.Value;

    /// <summary>
    /// Uses <see cref="LazyThreadSafetyMode.PublicationOnly"/> so a failure while evaluating
    /// the license is not cached for the lifetime of the process
    /// </summary>
    private static Lazy<string[]> LicensedAPIExtensionsLazy = new(() =>
        new string?[] {
            LicenseHelper.IsOffice365Enabled ? "office365" : null
        }
        .WhereNotNull()
        .ToArray(),
        LazyThreadSafetyMode.PublicationOnly
    );
}

