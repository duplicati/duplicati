// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.LicenseChecker;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Shared list of statically linked source-provider modules
/// </summary>
public static class SourceProviderModules
{
    /// <summary>
    /// The list of all built-in source-provider modules
    /// </summary>
    public static IReadOnlyList<ISourceProviderModule> LicensedSourceProviderModules
    {
        get
        {
            // Safegard against errors during loading, e.g. missing libraries
            try
            {
                return LicensedSourceProvidersLazy.Value;
            }
            catch
            {
                return Array.Empty<ISourceProviderModule>();
            }
        }
    }

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static Lazy<IReadOnlyList<ISourceProviderModule>> LicensedSourceProvidersLazy = new(() =>
        new ISourceProviderModule?[] {
            LicenseHelper.IsOffice365Enabled ? new Office365.SourceProvider() : null,
            LicenseHelper.IsGoogleWorkspaceEnabled ? new GoogleWorkspace.SourceProvider() : null,
            new DiskImage.SourceProvider()
        }
        .WhereNotNull()
        .ToList()
    );
}

