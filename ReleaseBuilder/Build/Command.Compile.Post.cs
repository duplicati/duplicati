using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Duplicati.Library.Utility;

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
        /// <param name="os">The target operating system</param>
        /// <param name="arch">The target architecture</param>
        /// <param name="buildTargetString">The build target string os-arch-interface</param>
        /// <param name="rtcfg">The runtime config</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <returns>An awaitable task</returns>
        public static async Task PrepareTargetDirectory(string baseDir, string buildDir, OSType os, ArchType arch, string buildTargetString, RuntimeConfig rtcfg, bool keepBuilds)
        {
            await InstallUplinkBinaries(buildDir, os, arch);
            await RemoveUnwantedFiles(os, buildDir);

            switch (os)
            {
                case OSType.Windows:
                    await SignWindowsExecutables(buildDir, rtcfg);
                    break;

                case OSType.MacOS:
                    await BundleMacOSApplication(baseDir, buildDir, buildTargetString, rtcfg, keepBuilds);
                    break;

                case OSType.Linux:
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
                OSType.MacOS => ["lvm-scripts", "win-tools"],
                OSType.Linux => ["win-tools"],
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
            var resourcesDir = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "MacOS");

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
            foreach (var x in ExecutableRenames)
                File.Move(Path.Combine(binDir, x.Key), Path.Combine(binDir, x.Value));

            // Move the licenses out of the code folder as the signing tool trips on it
            var licenseTarget = Path.Combine(tmpApp, "Contents", "Licenses");
            Directory.Move(Path.Combine(binDir, "licenses"), licenseTarget);

            if (rtcfg.UseCodeSignSigning)
            {
                Console.WriteLine("Performing MacOS code signing ...");

                // Executables cannot be signed before their dependencies are signed
                // So they are placed last in the list
                var executables = ExecutableRenames.Values.Select(x => Path.Combine(binDir, x));

                var signtargets = Directory.EnumerateFiles(binDir, "*", SearchOption.AllDirectories)
                    .Except(executables)
                    .Concat(executables)
                    .Distinct()
                    .ToList();

                var entitlementFile = Path.Combine(resourcesDir, "Entitlements.plist");
                foreach (var f in signtargets)
                    await rtcfg.Codesign(f, entitlementFile);

                await rtcfg.Codesign(Path.Combine(tmpApp), entitlementFile);
            }

            if (!OperatingSystem.IsWindows())
                foreach (var f in Directory.EnumerateFiles(binDir, "*.launchagent.plist", SearchOption.TopDirectoryOnly))
                    File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Directory.Move(tmpApp, appDir);
            Directory.Delete(Path.GetDirectoryName(tmpApp) ?? throw new Exception("Unexpected empty path"), true);
        }

        /// <summary>
        /// Signs all .exe and .dll files with Authenticode
        /// </summary>
        /// <param name="buildDir">The folder to sign files in</param>
        /// <param name="rtcfg">The runtime config</param>
        /// <returns>An awaitable task</returns>
        static async Task SignWindowsExecutables(string buildDir, RuntimeConfig rtcfg)
        {
            var cfg = Program.Configuration;
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

    /// <summary>
    /// Downloads a file from a URL and saves it to a destination path
    /// </summary>
    /// <param name="url">The URL to download from</param>
    /// <param name="destinationPath">The path to save the file to</param>
    /// <returns>An awaitable task</returns>
    static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer()
        });

        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var tf = new TempFile();
        using var fileStream = File.Create(tf);
        await response.Content.CopyToAsync(fileStream);

        File.Move(tf, destinationPath);
    }

    /// <summary>
    /// Due to an issue with the packages for Uplink.Net, we need to download the binaries and manully extract them
    /// </summary>
    /// <param name="buildDir">The build directory</param>
    /// <param name="os">The target operating system</param>
    /// <param name="arch">The target architecture</param>
    /// <returns>An awaitable task</returns>
    static async Task InstallUplinkBinaries(string buildDir, OSType os, ArchType arch)
    {
        var buildroot = Path.GetDirectoryName(buildDir) ?? throw new Exception("Bad build dir");
        var pkgfolder = Path.Combine(buildroot, "nuget");
        var pkgname = os switch
        {
            OSType.Windows => (Url: "https://www.nuget.org/api/v2/package/uplink.NET.Win/2.12.3363", File: "uplink.net.win.2.12.3363.nupkg"),
            OSType.MacOS => (Url: "https://www.nuget.org/api/v2/package/uplink.NET.Mac/2.12.3365", File: "uplink.net.mac.2.12.3365.nupkg"),
            OSType.Linux => (Url: "https://www.nuget.org/api/v2/package/uplink.NET.Linux/2.12.3365", File: "uplink.net.linux.2.12.3365.nupkg"),
            _ => (null, null)
        };

        var zipEntryName = os switch
        {
            OSType.Windows => arch switch
            {
                ArchType.x64 => "runtimes/win-x64/native/storj_uplink.dll",
                ArchType.x86 => "runtimes/win-x86/native/storj_uplink.dll",
                ArchType.Arm64 => "runtimes/win-arm64/native/storj_uplink.dll",
                _ => null
            },
            OSType.MacOS => arch switch
            {
                ArchType.x64 or ArchType.Arm64 => "runtimes/osx-x64/native/libstorj_uplink.dylib", // Dual-arch binary
                _ => null
            },
            OSType.Linux => arch switch
            {
                ArchType.x64 => "runtimes/linux-x64/native/storj_uplink.so",
                _ => null
            },
            _ => null
        };

        // Combination not supported
        if (pkgname.Url == null || pkgname.File == null || zipEntryName == null)
            return;

        var pkgpath = Path.Combine(pkgfolder, pkgname.File);
        if (!File.Exists(pkgpath))
        {
            if (!Directory.Exists(pkgfolder))
                Directory.CreateDirectory(pkgfolder);

            await DownloadFileAsync(
                pkgname.Url,
                pkgpath
            );
        }

        // Extract the binary and install it in the target folder
        using var archive = new ZipArchive(File.OpenRead(pkgpath));
        var entry = archive.GetEntry(zipEntryName);
        if (entry != null)
            entry.ExtractToFile(Path.Combine(buildDir, Path.GetFileName(zipEntryName)), true);
    }
}
