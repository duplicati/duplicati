// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Shared list of statically linked web modules
/// </summary>
public static class WebModules
{
    /// <summary>
    /// The list of all built-in web modules
    /// </summary>
    public static IReadOnlyList<IWebModule> LicensedWebModules
    {
        get
        {
            // Safegard against errors during loading, e.g. missing libraries
            try
            {
                return LicensedWebModulesLazy.Value;
            }
            catch
            {
                return Array.Empty<IWebModule>();
            }
        }
    }

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static Lazy<IReadOnlyList<IWebModule>> LicensedWebModulesLazy = new(() =>
        new IWebModule?[] {
            LicenseHelper.HasOffice365Feature ? new Office365.WebModule() : null
        }
        .WhereNotNull()
        .ToList()
    );
}

