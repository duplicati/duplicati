// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.LicenseChecker;

namespace Duplicati.Proprietary.LoaderHelper;

internal static class LicenseHelper
{
    public static LicenseData? LicenseData => licenseData.Value;

    public static bool HasOffice365Feature => HasFeature(DuplicatiLicenseFeatures.Office365);

    private static bool HasFeature(string feature)
    {
        var data = LicenseData;
        if (data == null)
            return false;

        return data.Features.ContainsKey(feature);
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
        return LicenseChecker.LicenseChecker.ObtainLicenseAsync(key, ct.Token).Await();
    });

}
