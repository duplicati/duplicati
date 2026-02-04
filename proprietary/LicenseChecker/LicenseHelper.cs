// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.LicenseChecker;

public static class LicenseHelper
{
    public static LicenseData? LicenseData => licenseData.Value;

    public static int AvailableOffice365UserSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Users);
    public static int AvailableOffice365GroupSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Groups);
    public static int AvailableOffice365SiteSeats => GetFeatureSeats(DuplicatiLicenseFeatures.Office365Sites);

    public static int AvailableGoogleWorkspaceUserSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceUsers);
    public static int AvailableGoogleWorkspaceGroupSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceGroups);
    public static int AvailableGoogleWorkspaceSharedDriveSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceSharedDrives);
    public static int AvailableGoogleWorkspaceSiteSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceSites);
    public static int AvailableGoogleWorkspaceOrganizationalUnitSeats => GetFeatureSeats(DuplicatiLicenseFeatures.GoogleWorkspaceOrganizationalUnits);

    public static bool IsOffice365Enabled =>
        AvailableOffice365UserSeats > 0 ||
        AvailableOffice365GroupSeats > 0 ||
        AvailableOffice365SiteSeats > 0;

    public static bool IsGoogleWorkspaceEnabled =>
        AvailableGoogleWorkspaceUserSeats > 0 ||
        AvailableGoogleWorkspaceGroupSeats > 0 ||
        AvailableGoogleWorkspaceSharedDriveSeats > 0 ||
        AvailableGoogleWorkspaceSiteSeats > 0 ||
        AvailableGoogleWorkspaceOrganizationalUnitSeats > 0;

    private static Dictionary<string, int> UnlicensedSeats = new Dictionary<string, int>
    {
        { DuplicatiLicenseFeatures.Office365Users, 5 },
        { DuplicatiLicenseFeatures.Office365Groups, 5 },
        { DuplicatiLicenseFeatures.Office365Sites, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceUsers, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceGroups, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceSharedDrives, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceSites, 5 },
        { DuplicatiLicenseFeatures.GoogleWorkspaceOrganizationalUnits, 5 },
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

    private static Lazy<LicenseData?> licenseData = new Lazy<LicenseData?>(() =>
    {
        string? key = null;

        // Check for a license file in the installation directory
        var keyfilepath = Path.Combine(UpdaterManager.INSTALLATIONDIR, "license.key");
        if (File.Exists(keyfilepath))
            key = $"file://{keyfilepath}";

        // Check for a license key in the environment variables
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("DUPLICATI_LICENSE_KEY");

        // No license key found
        if (string.IsNullOrWhiteSpace(key))
            return null;

        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return LicenseChecker.ObtainLicenseAsync(key, ct.Token).Await();
    });

}
