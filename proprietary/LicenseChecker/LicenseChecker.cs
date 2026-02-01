// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using JsonSignature;
using System.Net.Http.Json;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.LicenseChecker;

/// <summary>
/// Class for permitting license checks
/// </summary>
public static class LicenseChecker
{
    /// <summary>
    /// The log tag for license checker
    /// </summary>
    public static string LOGTAG = Log.LogTagFromType(typeof(LicenseChecker));

    /// <summary>
    /// The license server URL
    /// </summary>
    public static string ServerUrl
    {
        get
        {
#if DEBUG
            var envUrl = Environment.GetEnvironmentVariable("DUPLICATI_LICENSE_SERVER_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
                return envUrl;
#endif
            return "https://licenses.duplicati.com/obtain-license";
        }
    }

    /// <summary>
    /// The license request data sent to the license server
    /// </summary>
    /// <param name="LicenseKey">The license key</param>
    private sealed record LicenseRequestData(string LicenseKey);

    /// <summary>
    /// The JSON options
    /// </summary>
    private static readonly JsonSerializerOptions DefaultJsonOptions
        = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };


    /// <summary>
    /// Ensures that the license includes the required features
    /// </summary>
    /// <param name="license">The license data</param>
    /// <param name="requiredFeatures">The required features</param>
    public static void EnsureFeatures(this LicenseData license, params string[] requiredFeatures)
    {
        var features = new Dictionary<string, string>(license.Features ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var missing = requiredFeatures.Where(x => !features.ContainsKey(x)).ToList();
        if (missing.Count == 0)
            return;

        throw new InvalidLicenseException($"License does not include required features: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Obtains the license data for the given license key
    /// </summary>
    /// <param name="licenseKey">The key to check</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The license data if the license is valid</returns>
    public static async Task<LicenseData> ObtainLicenseAsync(string licenseKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new ArgumentException("License key cannot be null or empty", nameof(licenseKey));

        if (licenseKey.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var filePath = licenseKey[5..];
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("License file path cannot be null or empty", nameof(licenseKey));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("License file not found", Path.GetFullPath(filePath));

            await using var fileStream = File.OpenRead(filePath);
            return await CheckLicenseAsync(fileStream, PublicKey, cancellationToken);
        }
        else if (licenseKey.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            var base64Data = licenseKey[7..];
            if (string.IsNullOrWhiteSpace(base64Data))
                throw new ArgumentException("License base64 data cannot be null or empty", nameof(licenseKey));

            byte[] decodedData;
            try
            {
                decodedData = Convert.FromBase64String(base64Data);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("License base64 data is not valid base64", nameof(licenseKey), ex);
            }

            await using var memoryStream = new MemoryStream(decodedData);
            return await CheckLicenseAsync(memoryStream, PublicKey, cancellationToken);
        }
        else
        {
            using var httpClient = new HttpClient();
            using var content = JsonContent.Create(new LicenseRequestData(licenseKey));
            using var response = await httpClient.PostAsync(ServerUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(message))
                    throw new InvalidLicenseException($"Failed to obtain license: {message}");
                throw new InvalidLicenseException($"Failed to obtain license: {response.StatusCode} {response.ReasonPhrase}");
            }

            return await CheckLicenseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), PublicKey, cancellationToken);
        }
    }

    /// <summary>
    /// Gets the embedded public key used to verify licenses
    /// </summary>
    private static string PublicKey => LazyPublicKey.Value;

    /// <summary>
    /// Gets the embedded public key used to verify licenses
    /// </summary>
    private static Lazy<string> LazyPublicKey { get; } = new Lazy<string>(() =>
    {
#if DEBUG
        var envKey = Environment.GetEnvironmentVariable("DUPLICATI_LICENSE_PUBLIC_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;
#endif
        var assembly = typeof(LicenseChecker).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(".PublicKey.xml", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Embedded public key resource not found");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded public key resource not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// Checks the license from the provided stream using the given public key
    /// </summary>
    /// <param name="source">The license stream</param>
    /// <param name="publicKey">The public key to verify the license</param>
    /// <param name="cancellationToken">A cancellation token</param>
    /// <returns>The license data if the license is valid</returns>
    private static async Task<LicenseData> CheckLicenseAsync(Stream source, string publicKey, CancellationToken cancellationToken)
    {
        if (source == null)
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        if (string.IsNullOrWhiteSpace(publicKey))
            throw new ArgumentException("Public key cannot be null or empty", nameof(publicKey));

        try
        {
            var isValid = JSONSignature.VerifyAtLeastOne(source, [new JSONSignature.VerifyOperation(
                Algorithm: JSONSignature.RSA_SHA256, PublicKey: publicKey
            )]);

            if (!isValid)
                throw new InvalidLicenseException("Invalid license signature");

            var licenseData = await JsonSerializer.DeserializeAsync<LicenseData>(source, DefaultJsonOptions, cancellationToken);
            if (licenseData == null)
                throw new InvalidLicenseException("Failed to deserialize license data");

            if (!licenseData.IsValidNow && !licenseData.IsInGracePeriod)
                throw new InvalidLicenseException("License is not valid at the current date");

            if (licenseData.IsInGracePeriod)
                Log.WriteWarningMessage(LOGTAG, "LicenseExpired", null, "License expired on {ValidTo:yyyy-MM-dd}, but is within the grace period until {ExpirationWithGrace:yyyy-MM-dd}.", licenseData.ValidTo, licenseData.ValidToWithGrace);

            return licenseData;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to validate license", ex);
        }
    }
}
