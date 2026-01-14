// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Configuration support for proprietary modules
/// </summary>
public static class Configuration
{
    public static string[] LicensedAPIExtensions => LicensedAPIExtensionsLazy.Value;

    private static Lazy<string[]> LicensedAPIExtensionsLazy = new(() =>
        new string?[] {
            LicenseHelper.HasOffice365Feature ? "office365" : null
        }
        .WhereNotNull()
        .ToArray()
    );
}

