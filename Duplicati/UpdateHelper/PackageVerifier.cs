// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.


using System;
using System.IO;
using System.Security.Cryptography;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Re-verifies the downloaded package against the values from the signed manifest
/// </summary>
internal static class PackageVerifier
{
    /// <summary>
    /// Checks that the job is backed by a manifest signed by a trusted key and that the package
    /// file matches the entry in it, returning a problem description or null
    /// </summary>
    /// <param name="log">The log</param>
    /// <param name="job">The job</param>
    /// <returns>Null if the package is acceptable, otherwise the problem</returns>
    public static string? Verify(HelperLog log, UpdateJob job)
    {
        if (string.IsNullOrWhiteSpace(job.PackagePath))
            return "No package path was given";
        if (!File.Exists(job.PackagePath))
            return $"Package not found: {job.PackagePath}";
        if (string.IsNullOrWhiteSpace(job.PackageSha256))
            return "No package hash was given";
        if (string.IsNullOrWhiteSpace(job.SignedManifest))
            return "No signed manifest was given";

        // The hash in the job is only trusted once it is found in a manifest signed by a trusted key
        ManifestInfo manifest;
        try
        {
            manifest = SignedManifest.Verify(job.SignedManifest, TrustedKeys.Load(log, job.InstallDir));
        }
        catch (Exception ex)
        {
            return $"The update manifest could not be verified: {ex.Message}";
        }

        if (!string.Equals(manifest.Version, job.TargetVersion, StringComparison.Ordinal))
            return $"The signed manifest is for version {manifest.Version}, not {job.TargetVersion}";

        ManifestPackage? entry = null;
        foreach (var p in manifest.Packages ?? [])
            if (string.Equals(p.PackageTypeId, job.PackageTypeId, StringComparison.OrdinalIgnoreCase))
                entry = p;

        if (entry == null)
            return $"The signed manifest has no package of type {job.PackageTypeId}";
        if (entry.Length != job.PackageLength || !string.Equals(entry.SHA256, job.PackageSha256, StringComparison.Ordinal))
            return "The package hash in the job does not match the signed manifest";

        using var fs = File.OpenRead(job.PackagePath);
        return VerifyContent(fs, job);
    }

    /// <summary>
    /// Checks that an open package matches the length and hash in the job, which <see cref="Verify"/> has already
    /// matched against the signed manifest; returns a problem description or null
    /// </summary>
    /// <param name="stream">The package, positioned at the start</param>
    /// <param name="job">The job</param>
    /// <returns>Null if the content matches, otherwise the problem</returns>
    public static string? VerifyContent(Stream stream, UpdateJob job)
    {
        if (stream.Length != job.PackageLength)
            return $"Package length is {stream.Length}, expected {job.PackageLength}";

        var hash = Convert.ToBase64String(SHA256.HashData(stream));
        if (!string.Equals(hash, job.PackageSha256, StringComparison.Ordinal))
            return "Package SHA256 does not match the signed manifest";

        return null;
    }
}
