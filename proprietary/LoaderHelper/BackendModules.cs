// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LoaderHelper;

/// <summary>
/// Shared list of statically linked backend modules
/// </summary>
public static class BackendModules
{
    /// <summary>
    /// The list of all built-in backend modules
    /// </summary>
    public static IReadOnlyList<IBackend> LicensedBackendModules => LicensedBackendModulesLazy.Value;

    /// <summary>
    /// Calculate list once and cache it
    /// </summary>
    private static Lazy<IReadOnlyList<IBackend>> LicensedBackendModulesLazy = new(() =>
        new IBackend?[] {
        }
        .WhereNotNull()
        .ToList()
    );
}

