// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Shared list of statically linked restore-destination-provider modules
/// </summary>
public static class RestoreDestinationProviderModules
{
    /// <summary>
    /// The list of all built-in restore-destination-provider modules
    /// </summary>
    public static IReadOnlyList<IRestoreDestinationProviderModule> LicensedRestoreDestinationProviderModules
    {
        get
        {
            // Safegard against errors during loading, e.g. missing libraries
            try
            {
                return LicensedRestoreDestinationProvidersLazy.Value;
            }
            catch
            {
                return Array.Empty<IRestoreDestinationProviderModule>();
            }
        }
    }

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static readonly Lazy<IReadOnlyList<IRestoreDestinationProviderModule>> LicensedRestoreDestinationProvidersLazy = new(() =>
        new IRestoreDestinationProviderModule?[] {
            //LicenseHelper.HasOffice365Feature ? new Office365.RestoreProvider() : null
        }
        .WhereNotNull()
        .ToList()
    );
}

