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
using System.Text.RegularExpressions;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Helper methods cleaning and signing build outputs
    /// </summary>
    private static class PostCompile
    {
        /// <summary>
        /// Prepares a target directory with fixes that are done post-build, but before making the individual packages
        /// </summary>
        /// <param name="baseDir">The source directory</param>
        /// <param name="buildDir">The output build directory to modify</param>
        /// <param name="target">The target to prepare for</param>
        /// <param name="buildTargetString">The build target string os-arch-interface</param>
        /// <param name="rtcfg">The runtime config</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <returns>An awaitable task</returns>
        public static async Task PrepareTargetDirectory(string baseDir, string buildDir, PackageTarget target, RuntimeConfig rtcfg, bool keepBuilds)
        {
            await RemoveUnwantedFiles(target.OS, buildDir);

            switch (target.OS)
            {
                case OSType.Windows:
                    await SignWindowsExecutables(buildDir, rtcfg);
                    break;

                case OSType.MacOS:
                    if (target.Interface == InterfaceType.GUI)
                        await BundleMacOSApplication(baseDir, buildDir, target.BuildTargetString, rtcfg, keepBuilds);
                    break;

                case OSType.Linux:
                    await ReplaceLibMonoUnix(baseDir, buildDir, target.Arch);
                    await ReplaceSQLiteInterop(baseDir, buildDir, target.Arch);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Set of files that are unwanted despite the OS
        /// </summary>
        static readonly IReadOnlyList<string> UnwantedCommonFiles = [
            "System.Reactive.xml" // Extra documentation file
        ];

        /// <summary>
        /// Set of folders that are unwanted despite the OS
        /// </summary>
        static readonly IReadOnlyList<string> UnwantedCommonFolders = [
            "Duplicati", // Debug folder that gets picked up during builds
            "control_dir", // Debug folder for lock files
        ];

        /// <summary>
        /// A list of folders that are unwanted for a given OS target
        /// </summary>
        /// <param name="os">The OS to get unwanted folders for</param>
        /// <returns>The unwanted folders</returns>
        static IEnumerable<string> UnwantedFolders(OSType os)
            => UnwantedCommonFolders.Concat(os switch
            {
                OSType.Windows => ["lvm-scripts"],
                OSType.MacOS => ["lvm-scripts"],
                OSType.Linux => [],
                _ => throw new Exception($"Not supported os: {os}")
            });

        /// <summary>
        /// A list of files that are unwanted for a given OS target
        /// </summary>
        /// <param name="os">The OS to get unwanted files for</param>
        /// <returns>The files that are unwanted</returns>
        static IEnumerable<string> UnwantedFiles(OSType os)
            => UnwantedCommonFiles.Concat(os switch
            {
                OSType.Windows => [],
                OSType.MacOS => [Path.Combine("utility-scripts", "DuplicatiVerify.ps1")],
                OSType.Linux => [Path.Combine("utility-scripts", "DuplicatiVerify.ps1")],
                _ => throw new Exception($"Not supported os: {os}")
            })
            .Distinct();


        /// <summary>
        /// The unwanted filenames
        /// </summary>
        /// <param name="os">The operating system to get the unwanted filenames for</param>
        /// <returns>The list of unwanted filenames</returns>
        static IEnumerable<string> UnwantedFileGlobExps(OSType os)
            => new[] {
            "Thumbs.db",
            "desktop.ini",
            ".DS_Store",
            "*.bak",
            "*.pdb",
            "*.mdb",
            "._*",
            os == OSType.Windows ? "*.sh" : "*.bat"
            };

        /// <summary>
        /// Returns a regular expression mapping files that are not wanted in the build folders
        /// </summary>
        /// <param name="os">The operating system to get the unwanted filenames for</param>
        /// <returns>A regular expression for matching unwanted filenames</returns>
        static Regex UnwantedFilePatterns(OSType os)
            => new Regex(@$"^({string.Join("|", UnwantedFileGlobExps(os).Select(x => x.Replace(".", "\\.").Replace("*", ".*")))})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Removes unwanted contents from the build folders
        /// </summary>
        /// <param name="os">The operating system the folder is targeting</param>
        /// <param name="buildDir">The directory where the build output is placed</param>
        /// <returns>An awaitable task</returns>
        static Task RemoveUnwantedFiles(OSType os, string buildDir)
        {
            foreach (var d in UnwantedFolders(os).Select(x => Path.Combine(buildDir, x)))
                if (Directory.Exists(d))
                    Directory.Delete(d, true);

            foreach (var f in UnwantedFiles(os).Select(x => Path.Combine(buildDir, x)))
                if (File.Exists(f))
                    File.Delete(f);

            var patterns = UnwantedFilePatterns(os);
            foreach (var f in Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories).Where(x => patterns.IsMatch(Path.GetFileName(x))))
                if (File.Exists(f))
                    File.Delete(f);


            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates the MacOS folder structure by moving all files into a .app folder
        /// </summary>
        /// <param name="baseDir">The source folder</param>
        /// <param name="buildDir">The MacOS build output</param>
        /// <param name="buildTargetString">The build-target string os-arch-interface</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <returns>An awaitable task</returns>
        static async Task BundleMacOSApplication(string baseDir, string buildDir, string buildTargetString, RuntimeConfig rtcfg, bool keepBuilds)
        {
            var buildroot = Path.GetDirectoryName(buildDir) ?? throw new Exception("Bad build dir");
            // Create target .app folder
            var appDir = Path.Combine(
                buildroot,
                $"{buildTargetString}-{rtcfg.MacOSAppName}"
            );

            if (Directory.Exists(appDir))
            {
                if (keepBuilds)
                {
                    Console.WriteLine("App folder already exsists, skipping MacOS application build");
                    return;
                }

                Directory.Delete(appDir, true);
            }

            // Prepare the .app folder structure
            var tmpApp = Path.Combine(buildroot, "tmpapp", rtcfg.MacOSAppName);

            var folders = new[] {
            Path.Combine("Contents"),
            Path.Combine("Contents", "MacOS"),
            Path.Combine("Contents", "Resources"),
        };

            if (Directory.Exists(tmpApp))
                Directory.Delete(tmpApp, true);

            Directory.CreateDirectory(tmpApp);
            foreach (var f in folders)
                Directory.CreateDirectory(Path.Combine(tmpApp, f));

            // Copy the primary contents into the binary folder
            var binDir = Path.Combine(tmpApp, "Contents", "MacOS");
            EnvHelper.CopyDirectory(buildDir, binDir, recursive: true);

            // Patch the plist and place the icon from the resources
            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "MacOS", "AppBundle");

            var plist = File.ReadAllText(Path.Combine(resourcesDir, "app-resources", "Info.plist"))
                .Replace("!LONG_VERSION!", rtcfg.ReleaseInfo.ReleaseName)
                .Replace("!SHORT_VERSION!", rtcfg.ReleaseInfo.Version.ToString());

            File.WriteAllText(
                Path.Combine(tmpApp, "Contents", "Info.plist"),
                plist
            );

            File.Copy(
                Path.Combine(resourcesDir, "app-resources", "Duplicati.icns"),
                Path.Combine(tmpApp, "Contents", "Resources", "Duplicati.icns"),
                overwrite: true
            );

            // Inject the launch agent
            EnvHelper.CopyDirectory(
                Path.Combine(resourcesDir, "daemon"),
                Path.Combine(tmpApp, "Contents", "Resources"),
                recursive: true
            );

            // Inject the uninstall.sh script
            File.Copy(
                Path.Combine(resourcesDir, "uninstall.sh"),
                Path.Combine(tmpApp, "Contents", "MacOS", "uninstall.sh"),
                overwrite: true
            );

            // Rename the executables, as symlinks are not supported in DMG files
            await PackageSupport.RenameExecutables(binDir);
            await PackageSupport.SetPermissionFlags(binDir, rtcfg);

            // Move the licenses out of the code folder as the signing tool trips on it
            var licenseTarget = Path.Combine(tmpApp, "Contents", "Licenses");
            Directory.Move(Path.Combine(binDir, "licenses"), licenseTarget);

            // Make files executable
            if (!OperatingSystem.IsWindows())
                foreach (var f in Directory.EnumerateFiles(binDir, "*.launchagent.plist", SearchOption.TopDirectoryOnly))
                    File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Directory.Move(tmpApp, appDir);
            Directory.Delete(Path.GetDirectoryName(tmpApp) ?? throw new Exception("Unexpected empty path"), true);
        }

        /// <summary>
        /// Replaces the library libMono.Unix.so with a version that has large file support for ARM7
        /// </summary>
        /// <param name="baseDir">The base directory</param>
        /// <param name="buildDir">The build directory</param>
        /// <param name="arch">The architecture to build for</param>
        /// <returns>An awaitable task</returns>
        static Task ReplaceLibMonoUnix(string baseDir, string buildDir, ArchType arch)
        {
            if (arch != ArchType.Arm7)
                return Task.CompletedTask;

            var sourceFile = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "linux-arm-binary", "libMono.Unix.so");
            var targetFile = Path.Combine(buildDir, "libMono.Unix.so");
            if (!File.Exists(targetFile))
                throw new Exception($"Expected file \"{targetFile}\" not found, has build changed?");

            File.Copy(sourceFile, targetFile, overwrite: true);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Replaces the library SQLiteInterop.dll with a version that is built against GLIBC_2.33 for ARM7
        /// </summary>
        /// <param name="baseDir">The base directory</param>
        /// <param name="buildDir">The build directory</param>
        /// <param name="arch">The architecture to build for</param>
        /// <returns>An awaitable task</returns>
        static Task ReplaceSQLiteInterop(string baseDir, string buildDir, ArchType arch)
        {
            if (arch != ArchType.Arm7)
                return Task.CompletedTask;

            var sourceFile = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "linux-arm-binary", "SQLite.Interop.dll");
            var targetFile = Path.Combine(buildDir, "SQLite.Interop.dll");
            if (!File.Exists(targetFile))
                throw new Exception($"Expected file \"{targetFile}\" not found, has build changed?");

            File.Copy(sourceFile, targetFile, overwrite: true);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Signs all .exe and .dll files with Authenticode
        /// </summary>
        /// <param name="buildDir">The folder to sign files in</param>
        /// <param name="rtcfg">The runtime config</param>
        /// <returns>An awaitable task</returns>
        static async Task SignWindowsExecutables(string buildDir, RuntimeConfig rtcfg)
        {
            var cfg = rtcfg.Configuration;
            if (!rtcfg.UseAuthenticodeSigning)
                return;

            var filenames = Directory.EnumerateFiles(buildDir, "Duplicati.*", SearchOption.TopDirectoryOnly)
                .Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Console.WriteLine($"Performing Authenticode signing of {filenames.Count} files");

            foreach (var file in filenames)
                await rtcfg.AuthenticodeSign(file);
        }
    }
}
