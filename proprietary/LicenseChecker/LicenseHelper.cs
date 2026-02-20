// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LicenseChecker;

public static class LicenseHelper
{
    private static readonly object _licenseLock = new();
    private static LicenseData? _cachedLicenseData;
    private static string? _remoteClientLicenseKey;
    private static bool _isInitialized;

    public static void SetRemoteClientLicenseKey(string? key)
    {
        lock (_licenseLock)
        {
            if (_remoteClientLicenseKey != key)
            {
                _remoteClientLicenseKey = key;
                _cachedLicenseData = null; // Force reload
                _isInitialized = false;
            }
        }
    }

    public static void ReloadLicense()
    {
        lock (_licenseLock)
        {
            _cachedLicenseData = null;
            _isInitialized = false;
        }
    }

    public static LicenseData? LicenseData
    {
        get
        {
            lock (_licenseLock)
            {
                if (!_isInitialized)
                {
                    _cachedLicenseData = LoadLicenseData();
                    _isInitialized = true;
                }
                return _cachedLicenseData;
            }
        }
    }

    private static LicenseData? LoadLicenseData()
    {
        // Priority: File > Environment > ClientLicenseKey (server-provided)
        // Local license always takes precedence over server-provided license
        string? key = null;

        // Check for a license file in the installation directory (highest priority)
        var keyfilepath = Path.Combine(UpdaterManager.INSTALLATIONDIR, "license.key");
        if (File.Exists(keyfilepath))
            key = $"file://{keyfilepath}";

        // Check for a license key in the environment variables
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("DUPLICATI_LICENSE_KEY");

        // Fall back to server-provided license key (lowest priority)
        if (string.IsNullOrWhiteSpace(key))
            key = _remoteClientLicenseKey;

        if (string.IsNullOrWhiteSpace(key))
            return null;

        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return LicenseChecker.ObtainLicenseAsync(key, ct.Token).Await();
    }

    /// <summary>
    /// Gets the local license data (from file or environment only, ignoring server-provided license).
    /// Used for SystemInfo to report local license status.
    /// </summary>
    public static LicenseData? GetLocalLicenseData()
    {
        string? key = null;

        // Check for a license file in the installation directory
        var keyfilepath = Path.Combine(UpdaterManager.INSTALLATIONDIR, "license.key");
        if (File.Exists(keyfilepath))
            key = $"file://{keyfilepath}";

        // Check for a license key in the environment variables
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("DUPLICATI_LICENSE_KEY");

        if (string.IsNullOrWhiteSpace(key))
            return null;

        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return LicenseChecker.ObtainLicenseAsync(key, ct.Token).Await();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the remote (server-provided) license data only.
    /// Used for OnConnect metadata to report remote license status.
    /// </summary>
    public static LicenseData? GetRemoteLicenseData()
    {
        string? key;
        lock (_licenseLock)
        {
            key = _remoteClientLicenseKey;
        }

        if (string.IsNullOrWhiteSpace(key))
            return null;

        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return LicenseChecker.ObtainLicenseAsync(key, ct.Token).Await();
        }
        catch
        {
            return null;
        }
    }

    public static int AvailableOffice365UserSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Users);
    public static int AvailableOffice365GroupSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Groups);
    public static int AvailableOffice365SiteSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Sites);

    public static int AvailableGoogleWorkspaceUserSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceUsers);
    public static int AvailableGoogleWorkspaceGroupSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceGroups);
    public static int AvailableGoogleWorkspaceSharedDriveSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceSharedDrives);
    public static int AvailableGoogleWorkspaceSiteSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceSites);

    public static bool IsOffice365Enabled =>
        AvailableOffice365UserSeats > 0 ||
        AvailableOffice365GroupSeats > 0 ||
        AvailableOffice365SiteSeats > 0;

    public static bool IsGoogleWorkspaceEnabled =>
        AvailableGoogleWorkspaceUserSeats > 0 ||
        AvailableGoogleWorkspaceGroupSeats > 0 ||
        AvailableGoogleWorkspaceSharedDriveSeats > 0 ||
        AvailableGoogleWorkspaceSiteSeats > 0;

    private static Dictionary<string, int> UnlicensedSeats = new Dictionary<string, int>
    {
        { DuplicatiLicenseFeatures.Office365Users, 5 },
        { DuplicatiLicenseFeatures.Office365Groups, 5 },
        { DuplicatiLicenseFeatures.Office365Sites, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceUsers, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceGroups, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceSharedDrives, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceSites, 5 }
    };

    private static int GetDefaultSeats(string feature)
        => UnlicensedSeats.GetValueOrDefault(feature, 0);

    private static int GetFeatureSeats(string feature)
    {
        var data = LicenseData;
        if (data == null)
            return GetDefaultSeats(feature);

        if (!int.TryParse(data.Features.GetValueOrDefault(feature, "0"), out int seats))
            return GetDefaultSeats(feature);

        return seats;
    }
}
