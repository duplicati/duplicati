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
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Implementations for the package builds
    /// </summary>
    private static class CreatePackage
    {
        /// <summary>
        /// Representation of a build package
        /// </summary>
        /// <param name="Target">The target package</param>
        /// <param name="CreatedFile">The created package file path</param>
        public record BuiltPackage(PackageTarget Target, string CreatedFile);

        /// <summary>
        /// Builds the packages for the specified build targets.
        /// </summary>
        /// <param name="baseDir">The base directory.</param>
        /// <param name="buildRoot">The build root directory.</param>
        /// <param name="buildTargets">The build targets.</param>
        /// <param name="keepBuilds">A flag indicating whether to keep the build files.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task<List<BuiltPackage>> BuildPackages(string baseDir, string buildRoot, IEnumerable<PackageTarget> buildTargets, bool keepBuilds, RuntimeConfig rtcfg)
        {
            var builtPackages = new List<BuiltPackage>();

            var packagesToBuild = buildTargets.Distinct().ToList();
            if (packagesToBuild.Count == 1)
                Console.WriteLine($"Building single package: {packagesToBuild.First().PackageTargetString}");
            else
                Console.WriteLine($"Building {packagesToBuild.Count} packages");

            // Build the packages, but skip Docker builds as they are bundled
            foreach (var target in packagesToBuild.Where(x => x.Package != PackageType.Docker))
            {
                Console.WriteLine($"Building {target.PackageTargetString} ...");
                builtPackages.Add(new BuiltPackage(target, await BuildPackage(baseDir, buildRoot, target, rtcfg, keepBuilds)));
                Console.WriteLine("Completed!");
            }

            // Build the Docker images with buildx for multi-arch support
            var dockerTargets = packagesToBuild.Where(x => x.Package == PackageType.Docker).ToList();
            if (dockerTargets.Count > 0)
            {
                var packageFolder = Path.Combine(buildRoot, "packages");
                if (!Directory.Exists(packageFolder))
                    Directory.CreateDirectory(packageFolder);


                var packageFiles = dockerTargets.Select(x => Path.Combine(packageFolder, $"duplicati-{rtcfg.ReleaseInfo.ReleaseName}-{x.PackageTargetString}"))
                    .ToList();

                if (packageFiles.All(File.Exists))
                {
                    Console.WriteLine("All docker images already exist, skipping Docker build");
                }
                else
                {
                    Console.WriteLine($"Building {dockerTargets.Count} Docker images ...");
                    await BuildDockerImages(baseDir, buildRoot, dockerTargets, rtcfg);

                    // Create the files
                    foreach (var f in packageFiles)
                        File.WriteAllText(f, "");
                }
            }

            return builtPackages;
        }

        /// <summary>
        /// Builds the package for the given target
        /// </summary>
        /// <param name="baseDir">The source folder base</param>
        /// <param name="releaseInfo">The release info to use</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        static async Task<string> BuildPackage(string baseDir, string buildRoot, PackageTarget target, RuntimeConfig rtcfg, bool keepBuilds)
        {
            var packageFolder = Path.Combine(buildRoot, "packages");
            if (!Directory.Exists(packageFolder))
                Directory.CreateDirectory(packageFolder);

            var packageFile = Path.Combine(packageFolder, $"duplicati-{rtcfg.ReleaseInfo.ReleaseName}-{target.PackageTargetString}");

            // Fix up non-conforming package names
            // Temporary disable
            // if (target.Package == PackageType.Deb)
            //     packageFile = Path.Combine(packageFolder, $"duplicati-{target.InterfaceString}-{rtcfg.ReleaseInfo.Version}_{target.ArchString}.deb");
            // if (target.Package == PackageType.RPM)
            //     packageFile = Path.Combine(packageFolder, $"duplicati-{target.InterfaceString}-{rtcfg.ReleaseInfo.Version}_{target.ArchString}.rpm");

            if (File.Exists(packageFile))
            {
                if (keepBuilds)
                {
                    Console.WriteLine($"Package file already exists, skipping package build for {target.PackageTargetString}");
                    return packageFile;
                }

                File.Delete(packageFile);
            }

            var tempFile = Path.Combine(packageFolder, $"tmp-{rtcfg.ReleaseInfo.ReleaseName}-{target.PackageTargetString}");
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            switch (target.Package)
            {
                case PackageType.Zip:
                    await BuildZipPackage(Path.Combine(buildRoot, target.BuildTargetString), $"duplicati-{rtcfg.ReleaseInfo.ReleaseName}-{target.BuildTargetString}", tempFile, target, rtcfg);
                    break;

                case PackageType.MSI:
                    await BuildMsiPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.DMG:
                    await BuildMacDmgPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.MacPkg:
                    if (target.Interface != InterfaceType.GUI)
                        await BuildMacNonAppPkgPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    else
                        await BuildMacAppPkgPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.Deb:
                    await BuildDebPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.RPM:
                    await BuildRpmPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                // case PackageType.SynologySpk:
                //     await BuildZipPackage(buildRoot, tempFile, target, rtcfg);
                //     await SignSynologyPackage(Path.Combine(outputFolder, target.PackageTargetString), rtcfg);
                //     break;

                default:
                    throw new Exception($"Unsupported package type: {target.Package}");
            }

            if (rtcfg.UseNotarizeSigning && (target.Package == PackageType.DMG || target.Package == PackageType.MacPkg))
            {
                // # Notarize and staple takes a while...
                Console.WriteLine($"Performing notarize and staple of {packageFile} ...");
                await ProcessHelper.Execute(["xcrun", "notarytool", "submit", tempFile, "--keychain-profile", rtcfg.Configuration.ConfigFiles.NotarizeProfile, "--wait"]);
                await ProcessHelper.Execute(["xcrun", "stapler", "staple", tempFile]);
            }


            File.Move(tempFile, packageFile);

            return packageFile;
        }

        /// <summary>
        /// Builds a ZIP package asynchronously.
        /// </summary>
        /// <param name="buildRoot">The output folder where the ZIP package will be created.</param>
        /// <param name="dirName">The directory name to use as the root ZIP name.</param>
        /// <param name="zipFile">The ZIP file to generate.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        static async Task BuildZipPackage(string buildRoot, string dirName, string zipFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            if (File.Exists(zipFile))
                File.Delete(zipFile);

            var executableExtensions = new HashSet<string>([".sh", ".bat", ".py", ".exe"], StringComparer.OrdinalIgnoreCase);
            var executables = new List<string>();

            using (ZipArchive zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.EnumerateFiles(buildRoot, "*", SearchOption.AllDirectories))
                {
                    var relpath = Path.GetRelativePath(buildRoot, f);
                    var isRenamedExecutable = ExecutableRenames.ContainsKey(relpath);

                    // Use more friendly names for executables on non-Windows platforms
                    if (target.OS != OSType.Windows && isRenamedExecutable)
                        relpath = ExecutableRenames[relpath];

                    var entry = zip.CreateEntry(Path.Combine(dirName, relpath), CompressionLevel.Optimal);

                    var isExecutable = isRenamedExecutable || executableExtensions.Contains(Path.GetExtension(f));

                    // Set execute/permission flags
                    entry.ExternalAttributes = isExecutable
                        ? Convert.ToInt32("755", 8) << 16
                        : Convert.ToInt32("644", 8) << 16;

                    if (isExecutable)
                        executables.Add(relpath);

                    using (var stream = entry.Open())
                    using (var file = File.OpenRead(f))
                        await file.CopyToAsync(stream);
                }

                // Write the package type identifier
                using (var stream = zip.CreateEntry(Path.Combine(dirName, "package_type_id.txt"), CompressionLevel.Optimal).Open())
                using (var writer = new StreamWriter(stream))
                    writer.WriteLine(target.PackageTargetString);

                if (target.OS != OSType.Windows)
                {
                    var setEntry = zip.CreateEntry(Path.Combine(dirName, "set-permissions.sh"), CompressionLevel.Optimal);
                    setEntry.ExternalAttributes = Convert.ToInt32("755", 8) << 16;

                    using (var stream = setEntry.Open())
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.WriteLine("#!/bin/sh");
                        writer.WriteLine("# This script sets the executable flags for the Duplicati binaries and support scripts");
                        writer.WriteLine("set -e");
                        foreach (var x in executables)
                            writer.WriteLine($"chmod +x {x}");
                    }
                }
            }
        }

        /// <summary>
        /// Builds an MSI package asynchronously.
        /// </summary>
        /// <param name="baseDir">The source base directory.</param>
        /// <param name="buildRoot">The root directory of the build.</param>
        /// <param name="msiFile">The MSI file to generate.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task BuildMsiPackage(string baseDir, string buildRoot, string msiFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            var isWindows = OperatingSystem.IsWindows();
            const string originalNamespace = "http://schemas.microsoft.com/wix/2006/wi";
            const string wixv4Namespace = "http://wixtoolset.org/schemas/v4/wxs";

            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "Windows");
            var resourcesSubDir = Path.Combine(resourcesDir,
                target.Interface switch
                {
                    InterfaceType.GUI => "TrayIcon",
                    InterfaceType.Agent => "Agent",
                    _ => throw new Exception($"Unsupported interface type: {target.Interface}")
                });

            var buildTmp = Path.Combine(buildRoot, "tmp-msi");
            if (Directory.Exists(buildTmp))
                Directory.Delete(buildTmp, true);

            EnvHelper.CopyDirectory(Path.Combine(buildRoot, target.BuildTargetString), buildTmp, recursive: true);
            await PackageSupport.InstallPackageIdentifier(buildTmp, target);

            var sourceFiles = buildTmp;
            if (!sourceFiles.EndsWith(Path.DirectorySeparatorChar))
                sourceFiles += Path.DirectorySeparatorChar;

            var binFiles = Path.Combine(resourcesSubDir, "binfiles.wxs");
            if (File.Exists(binFiles))
                File.Delete(binFiles);

            File.WriteAllText(binFiles, WixHeatBuilder.CreateWixFilelist(
                sourceFiles,
                version: rtcfg.ReleaseInfo.Version.ToString(),
                wixNs: isWindows ? wixv4Namespace : originalNamespace
            ));

            var msiArch = target.Arch switch
            {
                ArchType.x86 => "x86",
                ArchType.x64 => "x64",
                ArchType.Arm64 => "x64", // Using x64 MSI for ARM64
                _ => throw new Exception($"Architeture not supported: {target.ArchString}")
            };

            // Prepare the Wix arguments, different for WiX (Windows) and wixl (Linux/MacOS)
            string[] wixArgs;

            if (isWindows)
            {
                // Update namespace in the .wxs files for WiX v4
                var shortcutsTempfile = Path.Combine(buildTmp, "Shortcuts.wxs");
                var entryTempfile = Path.Combine(buildTmp, "Duplicati.wxs");

                File.WriteAllText(shortcutsTempfile, File.ReadAllText(Path.Combine(resourcesSubDir, "Shortcuts.wxs"))
                    .Replace(originalNamespace, wixv4Namespace));

                File.WriteAllText(entryTempfile, File.ReadAllText(Path.Combine(resourcesSubDir, "Duplicati.wxs"))
                    .Replace(originalNamespace, wixv4Namespace));

                wixArgs = [
                    rtcfg.Configuration.Commands.Wix!,
                    "build",
                    "-define", $"HarvestPath={sourceFiles}",
                    "-arch", msiArch,
                    "-out", msiFile,
                    shortcutsTempfile,
                    binFiles,
                    entryTempfile
                ];
            }
            else
            {
                // wixl needs the UI extension
                wixArgs = [
                    rtcfg.Configuration.Commands.Wix!,
                    "--ext", "ui",
                    "--extdir", Path.Combine(resourcesDir, "WixUIExtension"),
                    "--define", $"HarvestPath={sourceFiles}",
                    "--arch", msiArch,
                    "--output", msiFile,
                    Path.Combine(resourcesSubDir, "Shortcuts.wxs"),
                    binFiles,
                    Path.Combine(resourcesSubDir, "Duplicati.wxs")
                ];
            }

            await ProcessHelper.Execute(wixArgs, workingDirectory: buildRoot);

            if (rtcfg.UseAuthenticodeSigning)
                await rtcfg.AuthenticodeSign(msiFile);

            Directory.Delete(buildTmp, true);
        }

        /// <summary>
        /// Install the package identifier into the app bundle, and performs resigning of the binaries
        /// </summary>
        /// <param name="appFolder">The folder where the app bundle is located</param>
        /// <param name="installerDir">The installer dir where the installer files are located</param>
        /// <param name="target">The package target to create the file for</param>
        /// <param name="rtcfg">The runtime config</param>
        /// <returns>An awaitable task</returns>
        static async Task PrepareAndSignAppBundle(string appFolder, string installerDir, PackageTarget target, RuntimeConfig rtcfg)
        {
            await PackageSupport.InstallPackageIdentifier(Path.Combine(appFolder, "Contents", "MacOS"), target);

            // After injecting the package_type_id, resign
            if (rtcfg.UseCodeSignSigning)
            {
                var entitlementFile = Path.Combine(installerDir, "Entitlements.plist");
                // In principle, it should be enough to deep sign the bundle, but the signtool is broken
                await PackageSupport.SignMacOSBinaries(rtcfg, Path.Combine(appFolder, "Contents", "MacOS"), entitlementFile);
                await rtcfg.Codesign(appFolder, false, entitlementFile);
                await rtcfg.VerifyCodeSign(appFolder);
            }
        }

        /// <summary>
        /// Builds a DMG package asynchronously.
        /// </summary>
        /// <param name="baseDir">The source base directory.</param>
        /// <param name="buildRoot">The root directory of the build.</param>
        /// <param name="dmgFile">The DMG file to generate.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task BuildMacDmgPackage(string baseDir, string buildRoot, string dmgFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            var mountDir = Path.Combine(buildRoot, "mount");
            if (Directory.Exists(mountDir))
            {
                await ProcessHelper.Execute(["hdiutil", "detach", mountDir, "-quiet", "-force",],
                    workingDirectory: buildRoot, codeIsError: _ => false);

                Directory.Delete(mountDir, false);
            }
            Directory.CreateDirectory(mountDir);

            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "MacOS", "AppBundle");
            var compressedDmg = Path.Combine(resourcesDir, "template.dmg.bz2");
            if (!File.Exists(compressedDmg))
                throw new FileNotFoundException($"Compressed dmg template file not found: {compressedDmg}");

            // Remove the bz2
            var templateDmg = Path.Combine(buildRoot, Path.GetFileNameWithoutExtension(compressedDmg));
            if (File.Exists(templateDmg))
                File.Delete(templateDmg);

            // Decompress the dmg
            using (var fs = File.Create(templateDmg))
                await ProcessHelper.ExecuteWithOutput([
                    "bzip2", "--decompress", "--keep", "--quiet", "--stdout", compressedDmg
                ], fs, workingDirectory: buildRoot);

            if (!File.Exists(templateDmg))
                throw new FileNotFoundException($"Decompressed dmg template file not found: {templateDmg}");

            await ProcessHelper.ExecuteAll([
                ["hdiutil", "resize", "-size", "300M", templateDmg],
                ["hdiutil", "attach", templateDmg, "-noautoopen", "-quiet", "-mountpoint", mountDir]
            ], workingDirectory: buildRoot);

            // Change the dmg name
            var dmgname = $"Duplicati {rtcfg.ReleaseInfo.ReleaseName}";
            Console.WriteLine($"Setting dmg name to {dmgname}");
            await ProcessHelper.Execute(["diskutil", "quiet", "rename", mountDir, dmgname], workingDirectory: mountDir);

            // Make the Duplicati.app structure, root folder should exist
            var appFolder = Path.Combine(mountDir, rtcfg.MacOSAppName);
            if (Directory.Exists(appFolder))
                Directory.Delete(appFolder, true);

            // Place the prepared folder
            EnvHelper.CopyDirectory(Path.Combine(buildRoot, $"{target.BuildTargetString}-{rtcfg.MacOSAppName}"), appFolder, recursive: true);
            await PrepareAndSignAppBundle(appFolder, resourcesDir, target, rtcfg);

            // Set permissions inside DMG file
            if (!OperatingSystem.IsWindows())
                await EnvHelper.Chown(appFolder, "root", "admin", true);

            // Unmount the dmg and compress
            await ProcessHelper.ExecuteAll([
                ["hdiutil", "detach", mountDir, "-quiet", "-force"],
            ["hdiutil", "convert", templateDmg, "-quiet", "-format", "UDZO", "-imagekey", "zlib-level=9", "-o", dmgFile]
            ], workingDirectory: buildRoot);

            // Clean up
            File.Delete(templateDmg);
            Directory.Delete(mountDir, false);

            await rtcfg.Codesign(dmgFile, false, Path.Combine(resourcesDir, "Entitlements.plist"));
            await rtcfg.VerifyCodeSign(dmgFile);
        }

        /// <summary>
        /// Builds the Mac package asynchronously.
        /// </summary>
        /// <param name="baseDir">The base directory.</param>
        /// <param name="buildRoot">The build root directory.</param>
        /// <param name="pkgFile">The package file path.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task BuildMacAppPkgPackage(string baseDir, string buildRoot, string pkgFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            var tmpFolder = Path.Combine(buildRoot, "tmp-pkg");
            if (Directory.Exists(tmpFolder))
                Directory.Delete(tmpFolder, true);
            Directory.CreateDirectory(tmpFolder);

            var installerDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "MacOS", "AppBundle");

            var appFolder = Path.Combine(tmpFolder, rtcfg.MacOSAppName);
            if (Directory.Exists(appFolder))
                Directory.Delete(appFolder, true);

            // Place the prepared folder
            EnvHelper.CopyDirectory(Path.Combine(buildRoot, $"{target.BuildTargetString}-{rtcfg.MacOSAppName}"), appFolder, recursive: true);
            await PrepareAndSignAppBundle(appFolder, installerDir, target, rtcfg);

            // Copy the source script files
            var scripts = new[] { "daemon", "daemon-scripts", "app-scripts" };

            // Copy scripts
            foreach (var s in scripts)
                EnvHelper.CopyDirectory(Path.Combine(installerDir, s), Path.Combine(tmpFolder, s), recursive: true);

            // Set permissions
            if (!OperatingSystem.IsWindows())
            {
                await EnvHelper.Chown(appFolder, "root", "admin", true);
                foreach (var f in Directory.EnumerateFiles(Path.Combine(tmpFolder, "daemon"), "*.launchagent.plist", SearchOption.AllDirectories))
                    await EnvHelper.Chown(f, "root", "wheel", false);

                var filemode = EnvHelper.GetUnixFileMode("+x");
                var allscripts = scripts.Select(x => Path.Combine(tmpFolder, x)).Where(Directory.Exists).SelectMany(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories));
                foreach (var x in allscripts)
                    if (File.Exists(x))
                        EnvHelper.AddFilemode(x, filemode);
            }

            var pkgAppFile = Path.Combine(tmpFolder, $"{rtcfg.ReleaseInfo.ReleaseName}-DuplicatiApp.pkg");
            if (File.Exists(pkgAppFile))
                File.Delete(pkgAppFile);
            var pkgDaemonFile = Path.Combine(tmpFolder, $"{rtcfg.ReleaseInfo.ReleaseName}-DuplicatiDaemon.pkg");
            if (File.Exists(pkgDaemonFile))
                File.Delete(pkgDaemonFile);

            var distributionFile = Path.Combine(tmpFolder, "Distribution.xml");
            var hostArch = target.Arch switch
            {
                ArchType.x86 => "i386",
                ArchType.x64 => "x86_64",
                ArchType.Arm64 => "arm64",
                _ => throw new Exception($"Unsupported architecture: {target.ArchString}")
            };

            File.WriteAllText(distributionFile,
                File.ReadAllText(Path.Combine(installerDir, "Distribution.xml"))
                    .Replace("DuplicatiApp.pkg", Path.GetFileName(pkgAppFile))
                    .Replace("DuplicatiDaemon.pkg", Path.GetFileName(pkgDaemonFile))
                    .Replace("$HOSTARCH", hostArch)
            );

            // Make the pkg files
            await ProcessHelper.ExecuteAll([
                ["pkgbuild", "--analyze", "--root", appFolder, "--install-location", "/Applications/Duplicati.app", "InstallerComponent.plist"],
                ["pkgbuild", "--scripts", Path.Combine(tmpFolder, "app-scripts"), "--identifier", "com.duplicati.app", "--root", appFolder, "--install-location", "/Applications/Duplicati.app", "--component-plist", "InstallerComponent.plist", pkgAppFile],
                ["pkgbuild", "--scripts", Path.Combine(tmpFolder, "daemon-scripts"), "--identifier", "com.duplicati.app.daemon", "--root", Path.Combine(tmpFolder, "daemon"), "--install-location", "/Library/LaunchAgents", pkgDaemonFile],
                ["productbuild", "--distribution", distributionFile, "--package-path", ".", "--resources", ".", pkgFile]
            ], workingDirectory: tmpFolder);

            // Clean up
            Directory.Delete(tmpFolder, true);

            // Sign the pkg file
            if (rtcfg.UseCodeSignSigning)
                await rtcfg.Productsign(pkgFile);
        }

        /// <summary>
        /// Builds the Mac package asynchronously.
        /// </summary>
        /// <param name="baseDir">The base directory.</param>
        /// <param name="buildRoot">The build root directory.</param>
        /// <param name="pkgFile">The package file path.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task BuildMacNonAppPkgPackage(string baseDir, string buildRoot, string pkgFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            var tmpFolder = Path.Combine(buildRoot, "tmp-pkg");
            if (Directory.Exists(tmpFolder))
                Directory.Delete(tmpFolder, true);
            Directory.CreateDirectory(tmpFolder);

            var resFolder = target.Interface switch
            {
                InterfaceType.Agent => "Agent",
                InterfaceType.Cli => "CLI",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            var installerDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "MacOS", resFolder);

            var binFolder = Path.Combine(tmpFolder, "bin");
            if (Directory.Exists(binFolder))
                Directory.Delete(binFolder, true);

            // Place the prepared folder
            EnvHelper.CopyDirectory(Path.Combine(buildRoot, $"{target.BuildTargetString}"), binFolder, recursive: true);

            // Rename the executables
            await PackageSupport.RenameExecutables(binFolder);
            await PackageSupport.SetPermissionFlags(binFolder, rtcfg);

            // Copy the source script files
            var scripts = new[] { "daemon", "daemon-scripts", "app-scripts" };

            // Copy scripts
            foreach (var s in scripts)
                EnvHelper.CopyDirectory(Path.Combine(installerDir, s), Path.Combine(tmpFolder, s), recursive: true);

            // Inject the launch agent
            EnvHelper.CopyDirectory(
                Path.Combine(installerDir, "daemon"),
                Path.Combine(binFolder),
                recursive: true
            );

            // Inject the uninstall.sh script
            File.Copy(
                Path.Combine(installerDir, "uninstall.sh"),
                Path.Combine(binFolder, "uninstall.sh"),
                overwrite: true
            );

            // Create symlinks for the executables
            if (target.Interface == InterfaceType.Cli)
            {
                var installedExecutables = ExecutableRenames.Values
                    .Select(x => Path.Combine(binFolder, x))
                    .Where(File.Exists)
                    .Select(Path.GetFileName)
                    .ToList();

                var installScript = Path.Combine(tmpFolder, "app-scripts", "postinstall");
                var installCommands = new StringBuilder();
                installCommands.Append(File.ReadAllText(installScript));
                installCommands.AppendLine();
                installCommands.AppendLine("echo 'Creating symbolic links for the Duplicati executables'");
                foreach (var x in installedExecutables)
                    installCommands.AppendLine($"ln -s /usr/local/duplicati/{x} /usr/local/bin/{x}");
                installCommands.AppendLine("echo 'Duplicati CLI has been installed'");
                installCommands.AppendLine("exit 0");
                File.WriteAllText(installScript, installCommands.ToString());

                var uninstallScript = Path.Combine(binFolder, "uninstall.sh");
                var uninstallCommands = new StringBuilder();
                uninstallCommands.Append(File.ReadAllText(uninstallScript));
                uninstallCommands.AppendLine();
                uninstallCommands.AppendLine("echo 'Removing links for the Duplicati executables'");
                foreach (var x in installedExecutables)
                    uninstallCommands.AppendLine($"if [ -L /usr/local/bin/{x} ]; then rm /usr/local/bin/{x}; fi");
                uninstallCommands.AppendLine("echo 'Duplicati symlinks are removed'");
                uninstallCommands.AppendLine("exit 0");
                File.WriteAllText(uninstallScript, uninstallCommands.ToString());
            }

            // Copy the package identifier
            await PackageSupport.InstallPackageIdentifier(binFolder, target);

            // Set permissions
            if (!OperatingSystem.IsWindows())
            {
                await EnvHelper.Chown(binFolder, "root", "admin", true);
                foreach (var f in Directory.EnumerateFiles(Path.Combine(tmpFolder, "daemon"), "*.launchagent.plist", SearchOption.AllDirectories))
                    await EnvHelper.Chown(f, "root", "wheel", false);

                var filemode = EnvHelper.GetUnixFileMode("+x");
                var allscripts = scripts
                    .Select(x => Path.Combine(tmpFolder, x))
                    .Where(Directory.Exists)
                    .SelectMany(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories))
                    .Append(Path.Combine(tmpFolder, "uninstall.sh"));

                foreach (var x in allscripts)
                    if (File.Exists(x))
                        EnvHelper.AddFilemode(x, filemode);
            }

            // Apply code signing, if requested
            await PackageSupport.SignMacOSBinaries(rtcfg, binFolder, Path.Combine(installerDir, "Entitlements.plist"));

            var payloadPkgFile = target.Interface switch
            {
                InterfaceType.Agent => "DuplicatiAgent.pkg",
                InterfaceType.Cli => "DuplicatiCLI.pkg",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            var daemonPkgFile = target.Interface switch
            {
                InterfaceType.Agent => "DuplicatiAgentDaemon.pkg",
                InterfaceType.Cli => "DuplicatiServerDaemon.pkg",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            var pkgAppFile = Path.Combine(tmpFolder, $"{rtcfg.ReleaseInfo.ReleaseName}-{payloadPkgFile}");
            if (File.Exists(pkgAppFile))
                File.Delete(pkgAppFile);
            var pkgDaemonFile = Path.Combine(tmpFolder, $"{rtcfg.ReleaseInfo.ReleaseName}-{daemonPkgFile}");
            if (File.Exists(pkgDaemonFile))
                File.Delete(pkgDaemonFile);

            var distributionFile = Path.Combine(tmpFolder, "Distribution.xml");
            var hostArch = target.Arch switch
            {
                ArchType.x86 => "i386",
                ArchType.x64 => "x86_64",
                ArchType.Arm64 => "arm64",
                _ => throw new Exception($"Unsupported architecture: {target.ArchString}")
            };

            File.WriteAllText(distributionFile,
                File.ReadAllText(Path.Combine(installerDir, "Distribution.xml"))
                    .Replace(payloadPkgFile, Path.GetFileName(pkgAppFile))
                    .Replace(daemonPkgFile, Path.GetFileName(pkgDaemonFile))
                    .Replace("$HOSTARCH", hostArch)
            );

            var installLocation = target.Interface switch
            {
                InterfaceType.Agent => "/usr/local/duplicati-agent",
                InterfaceType.Cli => "/usr/local/duplicati",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            var appId = target.Interface switch
            {
                InterfaceType.Agent => "com.duplicati.agent",
                InterfaceType.Cli => "com.duplicati.cli",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            var daemonId = target.Interface switch
            {
                InterfaceType.Agent => "com.duplicati.agent.daemon",
                InterfaceType.Cli => "com.duplicati.server.daemon",
                _ => throw new Exception($"Unsupported interface type: {target.Interface}")
            };

            // Make the pkg files
            await ProcessHelper.ExecuteAll([
                ["pkgbuild", "--analyze", "--root", binFolder, "--install-location", installLocation, "InstallerComponent.plist"],
                ["pkgbuild", "--scripts", Path.Combine(tmpFolder, "app-scripts"), "--identifier", appId, "--root", binFolder, "--install-location", installLocation, "--component-plist", "InstallerComponent.plist", pkgAppFile],
                ["pkgbuild", "--scripts", Path.Combine(tmpFolder, "daemon-scripts"), "--identifier", daemonId, "--root", Path.Combine(tmpFolder, "daemon"), "--install-location", "/Library/LaunchAgents", pkgDaemonFile],
                ["productbuild", "--distribution", distributionFile, "--package-path", ".", "--resources", ".", pkgFile]
            ], workingDirectory: tmpFolder);

            // Clean up
            Directory.Delete(tmpFolder, true);

            // Sign the pkg file
            if (rtcfg.UseCodeSignSigning)
                await rtcfg.Productsign(pkgFile);
        }

        /// <summary>
        /// Creates a GZip compressed file
        /// </summary>
        /// <param name="inputStream">The input stream path</param>
        /// <param name="outputFilePath">The output file path</param>
        /// <returns>A task representing the asynchronous operation</returns>
        static async Task GzipCompressFileAsync(Stream inputStream, string outputFilePath)
        {
            using (var outputFileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
            using (var gzipStream = new GZipStream(outputFileStream, CompressionMode.Compress))
                await inputStream.CopyToAsync(gzipStream);
        }

        /// <summary>
        /// Ensures the folder for the file exists
        /// </summary>
        /// <param name="filename">The filename</param>
        /// <returns>The filename</returns>
        public static string EnsureFolderForFile(string filename)
        {
            var folder = Path.GetDirectoryName(filename);
            if (folder != null && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            return filename;
        }

        /// <summary>
        /// Builds a DEB package using Docker
        /// </summary>
        /// <param name="baseDir">The base directory.</param>
        /// <param name="buildRoot">The build root directory.</param>
        /// <param name="debFile">The DEB file to generate.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task BuildDebPackage(string baseDir, string buildRoot, string debFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            // The approach here is based on:
            // https://www.internalpointers.com/post/build-binary-deb-package-practical-guide
            //
            // It is not the recommended way to build a package,
            // but since the build is from a pre-build binary,
            // it is easier than trying to hack debhelper.

            var debroot = Path.Combine(buildRoot, "deb");
            if (Path.Exists(debroot))
                Directory.Delete(debroot, true);
            Directory.CreateDirectory(debroot);

            // Make the package structure
            var debpkgdir = $"duplicati-{rtcfg.ReleaseInfo.Version}_{target.ArchString}";
            var pkgroot = Path.Combine(debroot, debpkgdir);

            Directory.CreateDirectory(pkgroot);
            Directory.CreateDirectory(Path.Combine(pkgroot, "DEBIAN"));
            Directory.CreateDirectory(Path.Combine(pkgroot, "usr", "lib"));
            Directory.CreateDirectory(Path.Combine(pkgroot, "usr", "bin"));

            // Copy main files
            EnvHelper.CopyDirectory(
                Path.Combine(buildRoot, target.BuildTargetString),
                Path.Combine(pkgroot, "usr", "lib", "duplicati"),
                recursive: true);

            var pkglib = Path.Combine(pkgroot, "usr", "lib", "duplicati");

            await PackageSupport.InstallPackageIdentifier(pkglib, target);
            await PackageSupport.RenameExecutables(pkglib);
            await PackageSupport.SetPermissionFlags(pkglib, rtcfg);

            if (OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Creating unresolved symlinks is not supported on Windows, use WSL or Docker");

            foreach (var e in ExecutableRenames.Values)
            {
                var exefile = Path.Combine(pkgroot, "usr", "lib", "duplicati", e);
                if (File.Exists(exefile))
                    await ProcessHelper.Execute([
                        "ln", "-s",
                        Path.Combine("..", "lib", "duplicati", e),
                        Path.Combine(pkgroot, "usr", "bin", e)
                    ]);
            }

            // Copy debian files
            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "debian");

            // Write the license file
            File.Copy(
                Path.Combine(baseDir, "LICENSE.txt"),
                EnsureFolderForFile(
                    Path.Combine(pkgroot, "usr", "share", "doc", "duplicati", "copyright")
                )
            );

            // Write a custom changelog file
            var changelogData = File.ReadAllText(Path.Combine(resourcesDir, "changelog.template.txt"))
                    .Replace("%VERSION%", rtcfg.ReleaseInfo.Version.ToString())
                    .Replace("%DATE%", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture));

            // Write a compressed changelog file
            using (var changelogStream = new MemoryStream(Encoding.UTF8.GetBytes(changelogData)))
                await GzipCompressFileAsync(
                    changelogStream,
                    EnsureFolderForFile(Path.Combine(pkgroot, "usr", "share", "doc", "duplicati", "changelog.gz"))
                );

            // Custom arch, from: https://wiki.debian.org/SupportedArchitectures
            var debArchString = target.Arch switch
            {
                ArchType.x86 => "i386",
                ArchType.x64 => "amd64",
                ArchType.Arm64 => "arm64",
                ArchType.Arm7 => "armhf",
                _ => throw new Exception($"Architeture not supported: {target.ArchString}")
            };

            var packageType = target.Interface switch
            {
                InterfaceType.Agent => "-agent",
                _ => ""
            };

            // Write a custom control file
            File.WriteAllText(
                Path.Combine(pkgroot, "DEBIAN", "control"),
                File.ReadAllText(Path.Combine(resourcesDir, "control.template.txt"))
                    .Replace("%VERSION%", rtcfg.ReleaseInfo.Version.ToString())
                    .Replace("%ARCH%", debArchString)
                    .Replace("%PACKAGE_TYPE%", packageType)
                    .Replace("%DEPENDS%", string.Join(", ", target.Interface == InterfaceType.GUI
                        ? DebianGUIDepends
                        : DebianCLIDepends))
            );

            // Install various helper files
            var sharedDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "shared");
            var supportFiles = new List<(string Source, string Destination)>();
            if (target.Interface != InterfaceType.Agent)
            {
                supportFiles.AddRange([
                (
                    Path.Combine(resourcesDir, "systemd", "duplicati.default"),
                    Path.Combine(pkgroot, "etc", "default", "duplicati")
                ),
                (
                    Path.Combine(resourcesDir, "systemd", "duplicati.service"),
                    Path.Combine(pkgroot, "lib", "systemd", "system", "duplicati.service")
                )
                ]);
            }
            else
            {
                supportFiles.AddRange([
                (
                    Path.Combine(resourcesDir, "systemd", "duplicati-agent.default"),
                    Path.Combine(pkgroot, "etc", "default", "duplicati-agent")
                ),
                (
                    Path.Combine(resourcesDir, "systemd", "duplicati-agent.service"),
                    Path.Combine(pkgroot, "lib", "systemd", "system", "duplicati-agent.service")
                )
                ]);

            }

            if (target.Interface == InterfaceType.GUI)
            {
                supportFiles.Add((
                    Path.Combine(sharedDir, "desktop", "duplicati.desktop"),
                    Path.Combine(pkgroot, "usr", "share", "applications", "duplicati.desktop")
                ));

                supportFiles.AddRange(
                    new[] { "duplicati.png", "duplicati.svg", "duplicati.xpm" }
                        .Select(f => (
                            Path.Combine(sharedDir, "pixmaps", f),
                            Path.Combine(pkgroot, "usr", "share", "pixmaps", f)
                        ))
                );
            }

            foreach (var f in supportFiles)
            {
                var dir = Path.GetDirectoryName(f.Destination);
                if (!Directory.Exists(dir) && dir != null)
                    Directory.CreateDirectory(dir);
                File.Copy(f.Source, f.Destination, true);

                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(f.Destination, UnixFileMode.OtherRead | UnixFileMode.GroupRead | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            // Write a conf file
            var confroot = Path.Combine(pkgroot, "etc");
            var conffiles = Directory.EnumerateFiles(confroot, "*", SearchOption.AllDirectories)
                .Select(x => "/" + Path.GetRelativePath(pkgroot, x))
                .Append(string.Empty)
                .ToList();

            File.WriteAllText(
                Path.Combine(pkgroot, "DEBIAN", "conffiles"),
                string.Join("\n", conffiles)
            );

            if (target.Interface == InterfaceType.Agent)
            {
                // Write a custom postinst script
                File.Copy(
                    Path.Combine(resourcesDir, $"postinst-agent"),
                    Path.Combine(pkgroot, "DEBIAN", "postinst"),
                    true
                );

                // Write a custom prerm script
                File.Copy(
                    Path.Combine(resourcesDir, $"prerm-agent"),
                    Path.Combine(pkgroot, "DEBIAN", "prerm"),
                    true
                );

                if (!OperatingSystem.IsWindows())
                {
                    var filemode = UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute;

                    File.SetUnixFileMode(Path.Combine(pkgroot, "DEBIAN", "prerm"), filemode);
                    File.SetUnixFileMode(Path.Combine(pkgroot, "DEBIAN", "postinst"), filemode);
                }
            }

            // Copy the Docker build file
            File.Copy(
                Path.Combine(resourcesDir, "Dockerfile.build"),
                Path.Combine(debroot, "Dockerfile"),
                true
            );

            // Build a Docker image to build with
            await ProcessHelper.Execute([
                "docker", "build",
                "-t", "duplicati/debian-build:latest",
                debroot
            ], workingDirectory: debroot);

            var debpkgname = $"{debpkgdir}.deb";

            // Docker desktop has some sync issues
            await Task.Delay(TimeSpan.FromSeconds(5));

            // Build in Docker
            await ProcessHelper.Execute([
                    "docker", "run",
                    "--workdir", $"/build",
                    "--volume", $"{debroot}:/build:rw", "duplicati/debian-build:latest",
                    // Using gzip compression for compatibility with older Debian versions
                    "dpkg-deb", "-Zgzip", "--build", "--root-owner-group", debpkgdir
            ]);

            File.Move(Path.Combine(debroot, debpkgname), debFile);
            Directory.Delete(debroot, true);
        }
    }

    /// <summary>
    /// Builds the RPM package using Docker
    /// </summary>
    /// <param name="baseDir">The base directory.</param>
    /// <param name="buildRoot">The build root directory.</param>
    /// <param name="rpmFile">The RPM file to generate.</param>
    /// <param name="target">The package target.</param>
    /// <param name="rtcfg">The runtime configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    static async Task BuildRpmPackage(string baseDir, string buildRoot, string rpmFile, PackageTarget target, RuntimeConfig rtcfg)
    {
        var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "fedora");
        var tmpbuild = Path.Combine(buildRoot, "tmp-fedora");
        if (Directory.Exists(tmpbuild))
            Directory.Delete(tmpbuild, true);
        Directory.CreateDirectory(tmpbuild);

        var tarsrc = Path.Combine(tmpbuild, $"duplicati-{rtcfg.ReleaseInfo.Version}");
        EnvHelper.CopyDirectory(Path.Combine(buildRoot, target.BuildTargetString), tarsrc, recursive: true);

        await PackageSupport.InstallPackageIdentifier(tarsrc, target);
        await PackageSupport.RenameExecutables(tarsrc);
        await PackageSupport.SetPermissionFlags(tarsrc, rtcfg);

        var binaries = ExecutableRenames.Values.Where(x => File.Exists(Path.Combine(tarsrc, x))).ToList();

        var executables =
            binaries
            .Concat(Directory.GetFiles(tarsrc, "*.sh", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(tarsrc, x)))
            .Concat(Directory.GetFiles(tarsrc, "*.py", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(tarsrc, x)))
            .ToList();

        // Create the tarball
        var tarfile = Path.Combine(tmpbuild, $"duplicati-{rtcfg.ReleaseInfo.Version}.tar.bz2");
        await ProcessHelper.Execute(
            ["tar", "-cjf", tarfile, Path.GetFileName(tarsrc)],
            workingDirectory: Path.GetDirectoryName(tarsrc)
        );
        Directory.Delete(tarsrc, true);

        // Create rpmbuild structure
        var sources = Path.Combine(tmpbuild, "SOURCES");
        Directory.CreateDirectory(sources);

        File.Move(tarfile, Path.Combine(sources, Path.GetFileName(tarfile)));

        // Move in extra files for building
        var sharedDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "shared");
        File.Copy(Path.Combine(sharedDir, "pixmaps", "duplicati.xpm"), Path.Combine(sources, "duplicati.xpm"));
        File.Copy(Path.Combine(sharedDir, "pixmaps", "duplicati.png"), Path.Combine(sources, "duplicati.png"));
        File.Copy(Path.Combine(sharedDir, "desktop", "duplicati.desktop"), Path.Combine(sources, "duplicati.desktop"));
        File.Copy(Path.Combine(resourcesDir, "duplicati-install-recursive.sh"), Path.Combine(sources, "duplicati-install-recursive.sh"));
        if (target.Interface != InterfaceType.Agent)
        {
            File.Copy(Path.Combine(resourcesDir, "systemd", "duplicati.service"), Path.Combine(sources, "duplicati.service"));
            File.Copy(Path.Combine(resourcesDir, "systemd", "duplicati.default"), Path.Combine(sources, "duplicati.default"));
        }
        else
        {
            File.Copy(Path.Combine(resourcesDir, "systemd", "duplicati-agent.service"), Path.Combine(sources, "duplicati-agent.service"));
            File.Copy(Path.Combine(resourcesDir, "systemd", "duplicati-agent.default"), Path.Combine(sources, "duplicati-agent.default"));
            File.Copy(Path.Combine(resourcesDir, "systemd", "duplicati-agent.preset"), Path.Combine(sources, "duplicati-agent.preset"));
        }

        // Write custom script to install executable files
        File.WriteAllLines(
            Path.Combine(sources, "duplicati-install-binaries.sh"),
            File.ReadAllLines(Path.Combine(resourcesDir, "duplicati-install-binaries.sh"))
                .SelectMany(line =>
                {
                    if (line.StartsWith("REPL: "))
                        return executables.Select(x => line.Substring("REPL: ".Length).Replace("%SOURCE%", x).Replace("%TARGET%", x));
                    if (line.StartsWith("SYML: "))
                        return binaries.Select(x => line.Substring("SYML: ".Length).Replace("%SOURCE%", x).Replace("%TARGET%", x));
                    return [line];
                })
        );

        var rpmarch = target.Arch switch
        {
            ArchType.x64 => "x86_64",
            ArchType.Arm64 => "aarch64",
            ArchType.Arm7 => "armv7hl",
            _ => throw new Exception($"Unsupported arch: {target.Arch}")
        };

        var specData = File.ReadAllText(Path.Combine(resourcesDir, "duplicati.spec.template.txt"))
                .Replace("%BUILDDATE%", DateTime.UtcNow.ToString("yyyyMMdd"))
                .Replace("%BUILDVERSION%", rtcfg.ReleaseInfo.Version.ToString())
                .Replace("%BUILDTAG%", rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant())
                .Replace("%VERSION%", rtcfg.ReleaseInfo.Version.ToString())
                .Replace("%SERVICENAME%", target.Interface == InterfaceType.Agent ? "-agent" : "")
                .Replace("%PROVIDES%", string.Join("\n", executables.Select(x => $"Provides:\t{x}")))
                .Replace("%DEPENDS%", string.Join("\n",
                    (target.Interface == InterfaceType.GUI
                        ? FedoraGUIDepends
                        : FedoraCLIDepends).Select(x => $"Requires:\t{x}")));

        // Remove comment markers, or remove lines
        if (target.Interface == InterfaceType.GUI)
            specData = specData.Replace("#GUI_ONLY#", "");
        else
            specData = string.Join("\n", specData.Split('\n').Where(x => !x.StartsWith("#GUI_ONLY#")));

        if (target.Interface == InterfaceType.Agent)
            specData = specData.Replace("#AGENT_ONLY#", "");
        else
            specData = string.Join("\n", specData.Split('\n').Where(x => !x.StartsWith("#AGENT_ONLY#")));

        File.WriteAllText(
            Path.Combine(sources, "duplicati.spec"),
            specData
        );

        // Install the Docker build file
        File.Copy(
            Path.Combine(resourcesDir, "Dockerfile.build"),
            Path.Combine(tmpbuild, "Dockerfile"),
            true
        );

        // Build a Docker image to build with
        await ProcessHelper.Execute([
            "docker", "build",
                "-t", "duplicati/fedora-build:latest",
                tmpbuild
        ], workingDirectory: tmpbuild);

        // Install the build script
        // This is required because rpmbuild reads file mode
        // in a way that is not compatible with Docker desktop bind mounts
        File.Copy(
            Path.Combine(resourcesDir, "inside-docker.sh"),
            Path.Combine(tmpbuild, "inside-docker.sh"),
            true
        );

        // Then build the package itself
        await ProcessHelper.Execute([
            "docker", "run",
                "--workdir", "/build",
                "--volume", $"{tmpbuild}:/build:rw",
                "duplicati/fedora-build:latest",

                "/bin/bash", "/build/inside-docker.sh", "/build", rpmarch

                // Sadly, Docker desktop has some issues with permissions that causes wrong exe bits
                // which breaks the build checks, and produces incorrect packages
                // "rpmbuild", "-bb", "--target", rpmarch,
                // "--define", $"_topdir /build", "SOURCES/duplicati.spec"
        ]);

        File.Move(Path.Combine(tmpbuild, "build.rpm"), rpmFile);

        // Clean up
        Directory.Delete(tmpbuild, true);
    }

    /// <summary>
    /// Builds the Docker images for the specified targets with buildx
    /// </summary>
    /// <param name="baseDir">The base directory.</param>
    /// <param name="buildRoot">The build root directory.</param>
    /// <param name="targets">The package target.</param>
    /// <param name="rtcfg">The runtime configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task BuildDockerImages(string baseDir, string buildRoot, IEnumerable<PackageTarget> targets, RuntimeConfig rtcfg)
    {
        // Make sure any dangling buildx instances are removed
        try { await ProcessHelper.Execute([rtcfg.Configuration.Commands.Docker!, "buildx", "rm", "duplicati-builder"], codeIsError: _ => false, suppressStdErr: true); }
        catch { }

        // Prepare multi-build
        await ProcessHelper.Execute([rtcfg.Configuration.Commands.Docker!, "buildx", "create", "--use", "--name", "duplicati-builder"]);

        // Perform a distict build for each interface type, but keep the buildx instance
        foreach (var interfaceType in Enum.GetValues<InterfaceType>())
        {
            var buildTargets = targets.Where(x => x.Interface == interfaceType);
            if (!buildTargets.Any())
                continue;

            var dockerArchs = buildTargets.Select(target => target switch
            {
                PackageTarget { Arch: ArchType.x64, OS: OSType.Linux } => "linux/amd64",
                PackageTarget { Arch: ArchType.Arm64, OS: OSType.Linux } => "linux/arm64/v8",
                PackageTarget { Arch: ArchType.Arm7, OS: OSType.Linux } => "linux/arm/v7",
                _ => throw new Exception($"Unsupported Docker target: {target.OS}/{target.Arch} ({target.Interface})")
            })
            .ToList();

            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "Docker");
            var tmpbuild = Path.Combine(buildRoot, "tmp-docker");
            if (Directory.Exists(tmpbuild))
                Directory.Delete(tmpbuild, true);
            Directory.CreateDirectory(tmpbuild);

            // Copy in the source data
            foreach (var target in buildTargets)
            {
                // Mapping to the Docker TARGETARCH value
                var dockerShortArch = target.Arch switch
                {
                    ArchType.x64 => "amd64",
                    ArchType.Arm64 => "arm64",
                    ArchType.Arm7 => "arm",
                    _ => throw new Exception($"Unsupported Docker target: {target.Arch}")
                };

                var tgfolder = Path.Combine(tmpbuild, dockerShortArch);

                EnvHelper.CopyDirectory(Path.Combine(buildRoot, target.BuildTargetString), tgfolder, recursive: true);
                await PackageSupport.InstallPackageIdentifier(tgfolder, target);
                await PackageSupport.RenameExecutables(tgfolder);
                await PackageSupport.SetPermissionFlags(tgfolder, rtcfg);
            }

            var tags = new List<string> { rtcfg.ReleaseInfo.Channel.ToString(), rtcfg.ReleaseInfo.Version.ToString(), $"{rtcfg.ReleaseInfo.Version}-{rtcfg.ReleaseInfo.Channel}" };
            if (rtcfg.ReleaseInfo.Channel == ReleaseChannel.Stable)
                tags.Add("latest");

            if (!dockerArchs.Any())
                continue;

            var dockerFile = Path.Combine(resourcesDir, interfaceType == InterfaceType.Agent ? "Dockerfile-agent" : "Dockerfile");
            var repo = $"{rtcfg.DockerRepo}{(interfaceType == InterfaceType.Agent ? "-agent" : "")}";

            // Build the images
            var args = new List<string> { rtcfg.Configuration.Commands.Docker!, "buildx", "build" };
            args.AddRange(tags.SelectMany(x => new[] { "-t", $"{repo}:{x.ToLowerInvariant()}" }));
            args.AddRange([
                "--platform", string.Join(",", dockerArchs),
                "--build-arg", $"VERSION={rtcfg.ReleaseInfo.Version}",
                "--build-arg", $"CHANNEL={rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}",
                "--file", dockerFile,
                "--output", $"type=image,push={rtcfg.PushToDocker.ToString().ToLowerInvariant()}",
                "."
            ]);

            // Run the build
            await ProcessHelper.Execute(args, workingDirectory: tmpbuild);

            // Clean up
            Directory.Delete(tmpbuild, true);
        }

        // Clean up
        await ProcessHelper.Execute([rtcfg.Configuration.Commands.Docker!, "buildx", "rm", "duplicati-builder"]);
    }
}
