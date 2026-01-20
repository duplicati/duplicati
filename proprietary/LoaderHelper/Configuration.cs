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

    private static Lazy<string[]> LicensedAPIExtensionsLazy = new(() =>
        new string?[] {
            LicenseHelper.AvailableOffice365FeatureSeats > 0 ? "office365" : null
        }
        .WhereNotNull()
        .ToArray()
    );
}

