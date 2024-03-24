using System.Globalization;
using System.IO.Compression;

namespace ReleaseBuilder.CliCommand;

public static partial class Build
{
    /// <summary>
    /// Implementations for the package builds
    /// </summary>
    private static class CreatePackage
    {
        /// <summary>
        /// Builds the packages for the specified build targets.
        /// </summary>
        /// <param name="baseDir">The base directory.</param>
        /// <param name="buildRoot">The build root directory.</param>
        /// <param name="buildTargets">The build targets.</param>
        /// <param name="keepBuilds">A flag indicating whether to keep the build files.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task BuildPackages(string baseDir, string buildRoot, IEnumerable<PackageTarget> buildTargets, bool keepBuilds, RuntimeConfig rtcfg)
        {
            var packagesToBuild = buildTargets.Distinct().ToList();
            if (packagesToBuild.Count == 1)
                Console.WriteLine($"Building single package: {packagesToBuild.First().PackageTargetString}");
            else
                Console.WriteLine($"Building {packagesToBuild.Count} packages");

            foreach (var target in packagesToBuild)
            {
                Console.WriteLine($"Building {target.PackageTargetString} ...");
                await BuildPackage(baseDir, buildRoot, target, rtcfg, keepBuilds);
                Console.WriteLine("Completed!");
            }
        }

        /// <summary>
        /// Builds the package for the given target
        /// </summary>
        /// <param name="baseDir">The source folder base</param>
        /// <param name="releaseInfo">The release info to use</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        static async Task BuildPackage(string baseDir, string buildRoot, PackageTarget target, RuntimeConfig rtcfg, bool keepBuilds)
        {
            var packageFolder = Path.Combine(buildRoot, "packages");
            if (!Directory.Exists(packageFolder))
                Directory.CreateDirectory(packageFolder);

            var packageFile = Path.Combine(packageFolder, $"{rtcfg.ReleaseInfo.ReleaseName}-{target.PackageTargetString}");
            if (target.Package == PackageType.Deb)
                packageFile = $"duplicati-{rtcfg.ReleaseInfo.Version}_{target.ArchString}.deb";

            if (File.Exists(packageFile))
            {
                if (keepBuilds)
                {
                    Console.WriteLine($"Package file already exists, skipping package build for {target.PackageTargetString}");
                    return;
                }

                File.Delete(packageFile);
            }

            var tempFile = Path.Combine(packageFolder, $"tmp-{rtcfg.ReleaseInfo.ReleaseName}-{target.PackageTargetString}");
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            switch (target.Package)
            {
                case PackageType.Zip:
                    await BuildZipPackage(Path.Combine(buildRoot, $"{target.BuildTargetString}"), rtcfg.ReleaseInfo.ReleaseName, tempFile, target, rtcfg);
                    break;

                case PackageType.MSI:
                    await BuildMsiPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.DMG:
                    await BuildMacDmgPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.MacPkg:
                    await BuildMacPkgPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                case PackageType.Deb:
                    await BuildDebPackage(baseDir, buildRoot, tempFile, target, rtcfg);
                    break;

                // case PackageType.SynologySpk:
                //     await BuildZipPackage(buildRoot, tempFile, target, rtcfg);
                //     await SignSynologyPackage(Path.Combine(outputFolder, target.PackageTargetString), rtcfg);
                //     break;

                default:
                    throw new Exception($"Unsupported package type: {target.Package}");
            }

            File.Move(tempFile, packageFile);
        }

        /// <summary>
        /// Builds a zip package asynchronously.
        /// </summary>
        /// <param name="buildRoot">The output folder where the zip package will be created.</param>
        /// <param name="dirName">The directory name to use as the root zip name.</param>
        /// <param name="zipFile">The zip file to generate.</param>
        /// <param name="target">The package target.</param>
        /// <param name="rtcfg">The runtime configuration.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        static async Task BuildZipPackage(string buildRoot, string dirName, string zipFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            if (File.Exists(zipFile))
                File.Delete(zipFile);

            using (ZipArchive zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.EnumerateFiles(buildRoot, "*", SearchOption.AllDirectories))
                {
                    var relpath = Path.GetRelativePath(buildRoot, f);

                    // Use more friendly names for executables on non-Windows platforms
                    if (target.OS != OSType.Windows && ExecutableRenames.ContainsKey(relpath))
                        relpath = ExecutableRenames[relpath];

                    var entry = zip.CreateEntry(Path.Combine(dirName, relpath), CompressionLevel.Optimal);
                    using (var stream = entry.Open())
                    using (var file = File.OpenRead(f))
                        await file.CopyToAsync(stream);
                }
                if (target.OS != OSType.Windows)
                {
                    using (var stream = zip.CreateEntry(Path.Combine(dirName, "set-permissions.sh"), CompressionLevel.Optimal).Open())
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.WriteLine("#!/bin/sh");
                        writer.WriteLine("# This script sets the executable flags for the Duplicati binaries");
                        writer.WriteLine("set -e");
                        foreach (var x in ExecutableRenames.Values)
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
            var installerDir = Path.Combine(baseDir, "Installer", "Windows");
            var binFiles = Path.Combine(installerDir, "binfiles.wxs");

            var sourceFiles = Path.Combine(buildRoot, target.BuildTargetString);
            if (!sourceFiles.EndsWith(Path.DirectorySeparatorChar))
                sourceFiles += Path.DirectorySeparatorChar;

            File.WriteAllText(binFiles, WixHeatBuilder.CreateWixFilelist(sourceFiles));

            await ProcessHelper.Execute(new[] {
            Program.Configuration.Commands.Wix!,
            "--define", $"HarvestPath={sourceFiles}",
            "--arch", target.ArchString,
            "--output", msiFile,
            Path.Combine(installerDir, "Shortcuts.wxs"),
            binFiles,
            Path.Combine(installerDir, "Duplicati.wxs")
        }, workingDirectory: buildRoot);

            if (rtcfg.UseAuthenticodeSigning)
                await rtcfg.AuthenticodeSign(msiFile);
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
                await ProcessHelper.Execute([
                    "hdiutil", "detach", mountDir, "-quiet", "-force",
            ], workingDirectory: buildRoot, codeIsError: _ => false);

                Directory.Delete(mountDir, false);
            }
            Directory.CreateDirectory(mountDir);

            var installerDir = Path.Combine(baseDir, "Installer", "MacOS");
            var compressedDmg = Path.Combine(installerDir, "template.dmg.bz2");
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
            await ProcessHelper.Execute([
                "diskutil", "quiet", "rename", mountDir, dmgname
            ], workingDirectory: mountDir);

            // Make the Duplicati.app structure, root folder should exist
            var appFolder = Path.Combine(mountDir, MacOSAppName);
            if (Directory.Exists(appFolder))
                Directory.Delete(appFolder, true);

            // Place the prepared folder
            EnvHelper.CopyDirectory(Path.Combine(buildRoot, $"{target.BuildTargetString}-{MacOSAppName}"), appFolder, recursive: true);
            await PackageSupport.SetExecutableFlags(appFolder, rtcfg);
            await PackageSupport.MakeSymlinks(appFolder);

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

            if (rtcfg.UseCodeSignSigning)
                await rtcfg.Codesign(dmgFile, Path.Combine(installerDir, "Entitlements.plist"));
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
        static async Task BuildMacPkgPackage(string baseDir, string buildRoot, string pkgFile, PackageTarget target, RuntimeConfig rtcfg)
        {
            var tmpFolder = Path.Combine(buildRoot, "tmp-pkg");
            if (Directory.Exists(tmpFolder))
                Directory.Delete(tmpFolder, true);
            Directory.CreateDirectory(tmpFolder);

            var installerDir = Path.Combine(baseDir, "Installer", "MacOS");

            var appFolder = Path.Combine(tmpFolder, MacOSAppName);
            if (Directory.Exists(appFolder))
                Directory.Delete(appFolder, true);

            // Place the prepared folder
            EnvHelper.CopyDirectory(Path.Combine(buildRoot, $"{target.BuildTargetString}-{MacOSAppName}"), appFolder, recursive: true);
            await PackageSupport.SetExecutableFlags(appFolder, rtcfg);
            await PackageSupport.MakeSymlinks(appFolder);

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

            File.WriteAllText(distributionFile,
                File.ReadAllText(Path.Combine(installerDir, "Distribution.xml"))
                    .Replace("DuplicatiApp.pkg", Path.GetFileName(pkgAppFile))
                    .Replace("DuplicatiDaemon.pkg", Path.GetFileName(pkgDaemonFile))
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
            Directory.CreateDirectory(Path.Combine(pkgroot, "usr", "share", "applications"));
            Directory.CreateDirectory(Path.Combine(pkgroot, "usr", "share", "pixmaps"));

            // Copy main files
            EnvHelper.CopyDirectory(
                Path.Combine(buildRoot, target.BuildTargetString),
                Path.Combine(pkgroot, "usr", "lib", "duplicati"),
                recursive: true);

            // TODO: For improved Windows support, this can be done in Docker
            if (!OperatingSystem.IsWindows())
            {
                var roflags = UnixFileMode.OtherRead | UnixFileMode.GroupRead | UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var exflags = roflags | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

                // Set permissions
                foreach (var f in Directory.EnumerateFileSystemEntries(Path.Combine(pkgroot, "usr", "lib", "duplicati"), "*", SearchOption.AllDirectories))
                {
                    if (File.Exists(f))
                        File.SetUnixFileMode(f,
                            ExecutableRenames.ContainsKey(Path.GetFileName(f))
                                ? exflags
                                : roflags);
                    else if (Directory.Exists(f))
                        File.SetUnixFileMode(f, exflags);
                }

                foreach (var e in ExecutableRenames)
                {
                    var exefile = Path.Combine(pkgroot, "usr", "lib", "duplicati", e.Key);
                    if (File.Exists(exefile))
                        await ProcessHelper.Execute([
                            "ln", "-s",
                            exefile,
                            Path.Combine(pkgroot, "usr", "bin", e.Value)
                        ]);
                }
            }

            // Copy debian files
            var installerDir = Path.Combine(baseDir, "Installer", "debian");

            // Write in the release notes
            // File.WriteAllText(Path.Combine(debroot, "releasenotes.txt"), rtcfg.ReleaseNotes); 
            // touch "${DIRNAME}/releasenotes.txt"

            // Write a custom changelog file
            File.WriteAllText(
                Path.Combine(pkgroot, "DEBIAN", "changelog"),
                File.ReadAllText(Path.Combine(installerDir, "changelog.template.txt"))
                    .Replace("%VERSION%", rtcfg.ReleaseInfo.Version.ToString())
                    .Replace("%DATE%", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture))
            );

            // Write a custom control file
            File.WriteAllText(
                Path.Combine(pkgroot, "DEBIAN", "control"),
                File.ReadAllText(Path.Combine(installerDir, "control.template.txt"))
                    .Replace("%VERSION%", rtcfg.ReleaseInfo.Version.ToString())
                    .Replace("%ARCH%", target.ArchString)
                    .Replace("%DEPENDS%", string.Join(", ", target.Interface == InterfaceType.GUI
                        ? DebianGUIDepends
                        : DebianCLIDepends))
            );

            // Install various helper files
            File.Copy(
                Path.Combine(installerDir, "duplicati.desktop"),
                Path.Combine(pkgroot, "usr", "share", "applications", "duplicati.desktop"),
                true
            );

            foreach (var f in new[] { "duplicati.png", "duplicati.svg", "duplicati.xpm" })
                File.Copy(
                    Path.Combine(installerDir, f),
                    Path.Combine(pkgroot, "usr", "share", "pixmaps", f),
                    true
                );

            // Install the Docker build file
            File.Copy(
                Path.Combine(installerDir, "Dockerfile.build"),
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

            // Build in Docker
            await ProcessHelper.Execute([
                    "docker", "run",
                    "--workdir", $"/build",
                    "--volume", $"{debroot}:/build:rw", "duplicati/debian-build:latest",
                    "dpkg-deb", "--build", "--root-owner-group", debpkgdir
            ]);

            File.Move(Path.Combine(debroot, debpkgname), debFile);
            Directory.Delete(debroot, true);
        }
    }
}
