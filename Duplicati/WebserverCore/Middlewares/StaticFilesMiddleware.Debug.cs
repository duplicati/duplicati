// Copyright (C) 2025, The Duplicati Team
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
#if DEBUG
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.WebserverCore.Middlewares;

internal static class NpmSpaHelper
{
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(NpmSpaHelper));

    private sealed record PackageJson(Dictionary<string, string>? dependencies, string? packageId);
    private sealed record PackageLockJson(Dictionary<string, PackageInfo>? packages);
    private sealed record PackageInfo(string version, string resolved, string? integrity);
    private sealed record InstalledPackageJson(string version);
    public sealed record SpaConfig(FileInfo IndexFile, string BasePath);

    public static SpaConfig? InstallNpmPackage(string packageUrl, string targetPath)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        // Handle move not working across drive boundaries on Windows
        if (OperatingSystem.IsWindows() && Path.GetPathRoot(tempPath) != Path.GetPathRoot(targetPath))
        {
            tempPath = Path.GetFullPath(targetPath);
            if (targetPath.EndsWith(Path.DirectorySeparatorChar))
                tempPath = tempPath[..-1];
            tempPath += "-tmp";
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }

        try
        {
            // Download and extract the package
            Directory.CreateDirectory(tempPath);

            var tgzFile = Path.Combine(tempPath, "package.tgz");
            var tarFile = Path.ChangeExtension(tgzFile, ".tar");
            using (var client = new HttpClient())
            {
                var response = client.GetAsync(packageUrl).Await();
                response.EnsureSuccessStatusCode();

                using (var stream = response.Content.ReadAsStream())
                using (var fileStream = File.Create(tgzFile))
                    stream.CopyTo(fileStream);
            }

            // Extract the zipped contents
            using (var fsource = new FileStream(tgzFile, FileMode.Open, FileAccess.Read))
            using (var ftarget = new FileStream(tarFile, FileMode.Create, FileAccess.Write))
            using (var gzip = new GZipStream(new FileStream(tgzFile, FileMode.Open, FileAccess.Read), CompressionMode.Decompress))
                gzip.CopyTo(ftarget);

            var dest = Path.Combine(tempPath, "package");

            // Extract the tar entries into the temp folder
            using (var fsource = new FileStream(tarFile, FileMode.Open, FileAccess.Read))
            using (var reader = new TarReader(fsource))
                while (reader.GetNextEntry() is TarEntry tar)
                {
                    var path = Path.Combine(tempPath, tar.Name);

                    // Ensure the path is within the temp folder
                    if (!path.StartsWith(dest))
                        continue;

                    if (tar.EntryType == TarEntryType.Directory)
                        Directory.CreateDirectory(path);
                    else if (tar.EntryType == TarEntryType.RegularFile)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid path"));
                        tar.ExtractToFile(path, false);
                    }
                }

            // Read the package.json and check it looks correct
            var packageJson = JsonSerializer.Deserialize<InstalledPackageJson>(File.ReadAllText(Path.Combine(dest, "package.json")));
            if (packageJson == null || string.IsNullOrWhiteSpace(packageJson.version))
                return null;

            // Make room for the new package
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);

            var parentFolder = Path.GetDirectoryName(targetPath);
            if (parentFolder != null && !Directory.Exists(parentFolder))
                Directory.CreateDirectory(parentFolder);

            // Move the package to the target path
            Directory.Move(dest, targetPath);

            // Clean up
            Directory.Delete(tempPath, true);

            return new SpaConfig(new FileInfo(Path.Combine(targetPath, "index.html")), targetPath);
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "SpaDebugHelperFail", ex, "NPM SPA package installation failed");
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }

        return null;
    }

    /// <summary>
    /// Probes for a missing/outdates SPA in the target folder and installs it if found.
    /// </summary>
    /// <param name="basepath">The base path to probe</param>
    /// <returns>The SPA configuration if found, otherwise null</returns>
    public static SpaConfig? ProbeForNpmSpa(string basepath)
    {
        try
        {
            // Check if the package.json and package-lock.json files exist
            var packageFile = Path.Combine(basepath, "package.json");
            var packagelockFile = Path.Combine(basepath, "package-lock.json");

            if (!File.Exists(packageFile) || !File.Exists(packagelockFile))
                return null;

            var packageJson = JsonSerializer.Deserialize<PackageJson>(File.ReadAllText(packageFile));
            var packageLockJson = JsonSerializer.Deserialize<PackageLockJson>(File.ReadAllText(packagelockFile));

            var packageId = string.IsNullOrWhiteSpace(packageJson?.packageId) ? packageJson?.dependencies?.Keys.FirstOrDefault() : packageJson.packageId;
            if (string.IsNullOrWhiteSpace(packageId) || packageJson?.dependencies == null)
                return null;

            var version = packageJson.dependencies.GetValueOrDefault(packageId);
            if (string.IsNullOrWhiteSpace(version))
                return null;

            // Remove version prefix
            if (!char.IsAsciiDigit(version.First()))
                version = version[1..];

            var packageUrl = packageLockJson?.packages?.GetValueOrDefault($"node_modules/{packageId}")?.resolved;
            if (string.IsNullOrWhiteSpace(packageUrl))
                return null;

            // Package is not installed, install it
            var packageFolder = Path.GetFullPath(Path.Combine(basepath, "node_modules", packageId));
            if (!Directory.Exists(packageFolder))
                return InstallNpmPackage(packageUrl, packageFolder);

            // The installed package is missing version, reinstall
            var installedPackageJson = Path.Combine(packageFolder, "package.json");
            if (!File.Exists(installedPackageJson))
                return InstallNpmPackage(packageUrl, packageFolder);

            // The installed package is not the correct version, reinstall
            var installedJson = JsonSerializer.Deserialize<InstalledPackageJson>(File.ReadAllText(installedPackageJson));
            if (installedJson == null || string.IsNullOrWhiteSpace(installedJson.version) || installedJson.version != version)
                return InstallNpmPackage(packageUrl, packageFolder);

            // Package is installed and correct version
            return new SpaConfig(new FileInfo(Path.Combine(packageFolder, "index.html")), packageFolder);
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "SpaDebugHelperFail", ex, "NPM SPA package installation failed");
        }

        return null;
    }

}
#endif