using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace ReleaseBuilder.CliCommand;

/// <summary>
/// The build command implementation
/// </summary>
public static class Build
{
    /// <summary>
    /// The primary project to build
    /// </summary>
    private const string PrimaryProject = "Duplicati.GUI.TrayIcon.csproj";
    /// <summary>
    /// Projects that only makes sense for Windows
    /// </summary>
    private static readonly IReadOnlySet<string> WindowsOnlyProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.WindowsService.csproj" };

    /// <summary>
    /// Name of the app bundle for MacOS
    /// </summary>
    private const string MacOSAppName = "Duplicati.app";

    /// <summary>
    /// Setup of the current runtime information
    /// </summary>
    /// <param name="ReleaseInfo">The release info for the current build</param>
    /// <param name="KeyfilePassword">The keyfile password</param>
    private class RuntimeConfig
    {
        /// <summary>
        /// Constructs a new <see cref="RuntimeConfig"/>
        /// </summary>
        /// <param name="releaseInfo">The release info to use</param>
        /// <param name="keyfilePassword">The keyfile password to use</param>
        /// <param name="executables">The executables</param>
        public RuntimeConfig(ReleaseInfo releaseInfo, string keyfilePassword, IEnumerable<string> executables)
        {
            ReleaseInfo = releaseInfo;
            KeyfilePassword = keyfilePassword;
            ExecutableBinaries = executables;
        }

        /// <summary>
        /// The cached password for the pfx file
        /// </summary>
        private string? _pfxPassword = null;

        /// <summary>
        /// The release info for this run
        /// </summary>
        public ReleaseInfo ReleaseInfo { get; }

        /// <summary>
        /// The keyfile password for this run
        /// </summary>
        public string KeyfilePassword { get; }

        /// <summary>
        /// The executables that should exist in the build folder
        /// </summary>
        public IEnumerable<string> ExecutableBinaries { get; }

        /// <summary>
        /// Gets the PFX password and throws if not possible
        /// </summary>
        public string PfxPassword
            => string.IsNullOrWhiteSpace(_pfxPassword)
                ? _pfxPassword = GetAuthenticodePassword(KeyfilePassword)
                : _pfxPassword;

        /// <summary>
        /// Cache value for checking if authenticode signing is enabled
        /// </summary>
        private bool? _useAuthenticodeSigning;

        /// <summary>
        /// Checks if Authenticode signing should be enabled
        /// </summary>
        public void ToggleAuthenticodeSigning()
        {
            if (!_useAuthenticodeSigning.HasValue)
            {
                if (Program.Configuration.IsAuthenticodePossible())
                    _useAuthenticodeSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for osslsigncode, continue without signing executables?", "Y", "n") == "Y")
                    {
                        _useAuthenticodeSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for osslsigncode");
                }
            }
        }

        /// <summary>
        /// Cache value for checking if codesign is possible
        /// </summary>
        private bool? _useCodeSignSigning;

        /// <summary>
        /// Checks if codesign is enabled
        /// </summary>
        public void ToggleSignCodeSigning()
        {
            if (!_useCodeSignSigning.HasValue)
            {
                if (!OperatingSystem.IsMacOS())
                    _useCodeSignSigning = false;
                else if (Program.Configuration.IsCodeSignPossible())
                    _useCodeSignSigning = true;
                else
                {
                    if (ConsoleHelper.ReadInput("Configuration missing for signcode, continue without signing executables?", "Y", "n") == "Y")
                    {
                        _useCodeSignSigning = false;
                        return;
                    }

                    throw new Exception("Configuration is not set up for signcode");
                }
            }
        }

        /// <summary>
        /// Returns a value indicating if signcode is enabled
        /// </summary>
        public bool UseCodeSignSigning => _useCodeSignSigning!.Value;

        /// <summary>
        /// Returns a value indicating if authenticode signing is enabled
        /// </summary>
        public bool UseAuthenticodeSigning => _useAuthenticodeSigning!.Value;

        /// <summary>
        /// Decrypts the password file and returns the PFX password
        /// </summary>
        /// <param name="keyfilepassword">Password for the password file</param>
        /// <returns>The Authenticode password</returns>
        private string GetAuthenticodePassword(string keyfilepassword)
            => EncryptionHelper.DecryptPasswordFile(Program.Configuration.ConfigFiles.AuthenticodePasswordFile, keyfilepassword);

        /// <summary>
        /// Performs authenticode signing if enabled
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <returns>An awaitable task</returns>
        public Task AuthenticodeSign(string file)
            => UseAuthenticodeSigning
                ? ProcessRunner.OsslCodeSign(
                    Program.Configuration.Commands.OsslSignCode!,
                    Program.Configuration.ConfigFiles.AuthenticodePfxFile,
                    PfxPassword,
                    file)
                : Task.CompletedTask;

        /// <summary>
        /// Performs codesign on the given identity
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <param name="entitlements">The entitlements to apply</param>
        /// <returns>An awaitable task</returns>
        public Task Codesign(string file, string entitlements)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSCodeSign(
                    Program.Configuration.Commands.Codesign!,
                    Program.Configuration.ConfigFiles.CodesignIdentity,
                    entitlements,
                    file
                )
                : Task.CompletedTask;

    }

    /// <summary>
    /// Structure for keeping all variables for a single release
    /// </summary>
    /// <param name="Version">The version to use</param>
    /// <param name="Type">The release type</param>
    /// <param name="Timestamp">The release timestamp</param>
    private record ReleaseInfo(Version Version, ReleaseType Type, DateTime Timestamp)
    {
        /// <summary>
        /// Gets the string name for the release
        /// </summary>
        public string ReleaseName => $"{Version}_{Type.ToString().ToLowerInvariant()}_{Timestamp:yyy-MM-dd}";


        /// <summary>
        /// Create a new release info
        /// </summary>
        /// <param name="type">The release type</param>
        /// <param name="incVersion">The incremental version</param>
        /// <returns>The release info</returns>
        public static ReleaseInfo Create(ReleaseType type, int incVersion)
            => new ReleaseInfo(new Version(2, 0, 0, incVersion), type, DateTime.Today);
    }

    /// <summary>
    /// Creates the build command
    /// </summary>
    /// <returns>The command</returns>
    public static Command Create()
    {
        var buildTargetOption = new Option<PackageTarget[]>(
            name: "--targets",
            description: "The possible build targets, multiple arguments supported. Use the format os-arch.package, example: x64-win.msi.",
            parseArgument: arg =>
            {
                if (!arg.Tokens.Any())
                    return Program.SupportedPackageTargets.ToArray();

                var requested = arg.Tokens.Select(x => PackageTarget.ParsePackageId(x.Value)).Distinct().ToArray();
                var invalid = requested.Where(x => !Program.SupportedPackageTargets.Contains(x)).ToList();
                if (invalid.Any())
                    throw new Exception($"Following targets are not supported: {string.Join(", ", invalid.Select(x => x.PackageTargetString))}");

                return requested;
            });

        var releaseTypeOption = new Argument<ReleaseType>(
            name: "type",
            description: "The release type",
            getDefaultValue: () => ReleaseType.Canary
        );

        var gitStashPushOption = new Option<bool>(
            name: "--git-stash",
            description: "Performs a git stash command before running the build, and a git commit after updating files",
            getDefaultValue: () => true
        );

        var keepBuildsOption = new Option<bool>(
            name: "--keep-build",
            description: "Do not delete the build folders if they already exist (re-use build)",
            getDefaultValue: () => false
        );

        var buildTempOption = new Option<DirectoryInfo>(
            name: "--build-path",
            description: "The path to the temporary folder used for builds; will be deleted on startup",
            getDefaultValue: () => new DirectoryInfo(Path.GetFullPath("build-temp"))
        );

        var solutionFileOption = new Option<FileInfo>(
            name: "--solution-path",
            description: "Path to the Duplicati.sln file",
            getDefaultValue: () => new FileInfo(Path.GetFullPath(Path.Combine("..", "Duplicati.sln")))
        );

        var updateUrlsOption = new Option<string>(
            name: "--update-urls",
            description: "The updater urls where the client will check for updates",
            getDefaultValue: () => "https://updates.duplicati.com/${RELEASE_TYPE}/latest-v2.manifest;https://alt.updates.duplicati.com/${RELEASE_TYPE}/latest-v2.manifest"
        );

        var command = new Command("build", "Builds the packages for a release") {
            gitStashPushOption,
            releaseTypeOption,
            buildTempOption,
            buildTargetOption,
            solutionFileOption,
            updateUrlsOption,
            keepBuildsOption
        };

        command.SetHandler(async (buildTargets, buildTemp, solutionFile, gitStashPush, releaseType, updateUrls, keepBuilds) =>
        {
            Console.WriteLine($"Building {releaseType} release ...");

            if (!buildTargets.Any())
                buildTargets = Program.SupportedPackageTargets.ToArray();

            if (!solutionFile.Exists)
                throw new FileNotFoundException($"Solution file not found: {solutionFile.FullName}");

            var baseDir = Path.GetDirectoryName(solutionFile.FullName) ?? throw new Exception("Path to solution file was invalid");
            var versionFilePath = Path.Combine(baseDir, "Updates", "build_version.txt");
            if (!File.Exists(versionFilePath))
                throw new FileNotFoundException($"Version file not found: {versionFilePath}");

            var sourceProjects = Directory.EnumerateDirectories(Path.Combine(baseDir, "Executables", "net8"), "*", SearchOption.TopDirectoryOnly)
                .SelectMany(x => Directory.EnumerateFiles(x, "*.csproj", SearchOption.TopDirectoryOnly))
                .ToList();

            var primary = sourceProjects.FirstOrDefault(x => string.Equals(Path.GetFileName(x), PrimaryProject, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Failed to find tray icon executable");
            var windowsOnly = sourceProjects.Where(x => WindowsOnlyProjects.Contains(Path.GetFileName(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Put primary at the end
            sourceProjects.Remove(primary);
            sourceProjects.Add(primary);

            if (!File.Exists(primary))
                throw new Exception($"Failed to locate project file: {primary}");

            var releaseInfo = ReleaseInfo.Create(releaseType, int.Parse(File.ReadAllText(versionFilePath)) + 1);
            Console.WriteLine($"Building {releaseInfo.ReleaseName} ...");

            var keyfilePassword = ConsoleHelper.ReadPassword("Enter keyfile password");

            // Configure runtime environment
            var rtcfg = new RuntimeConfig(releaseInfo, keyfilePassword, sourceProjects.Select(x => Path.GetFileNameWithoutExtension(x)).ToList());
            rtcfg.ToggleAuthenticodeSigning();
            rtcfg.ToggleSignCodeSigning();

            if (!keepBuilds)
            {
                if (Directory.Exists(buildTemp.FullName))
                {
                    Console.WriteLine($"Deleting build folder: {buildTemp.FullName}");
                    Directory.Delete(buildTemp.FullName, true);
                }
            }

            if (!Directory.Exists(buildTemp.FullName))
                Directory.CreateDirectory(buildTemp.FullName);


            if (gitStashPush)
                await ProcessHelper.Execute(new[] { "git", "stash", "save", $"auto-build-{releaseInfo.Timestamp:yyyy-MM-dd}" }, workingDirectory: baseDir);

            await PrepareSourceDirectory(baseDir, releaseInfo, updateUrls);

            var logFolder = Path.Combine(buildTemp.FullName, "logs");
            Directory.CreateDirectory(logFolder);

            // Get the unique build targets (ignoring the package type)
            var buildArchTargets = buildTargets.DistinctBy(x => (x.OS, x.Arch)).ToArray();

            if (buildArchTargets.Length == 1)
                Console.WriteLine($"Building single release: {buildArchTargets.First().BuildArchString}");
            else
                Console.WriteLine($"Building {buildArchTargets.Length} versions");

            foreach (var target in buildArchTargets)
            {
                var outputFolder = Path.Combine(buildTemp.FullName, target.BuildArchString);
                if (keepBuilds && Directory.Exists(outputFolder))
                {
                    Console.WriteLine($"Skipping build as output exists for {target.BuildArchString}");
                }
                else
                {
                    Console.WriteLine($"Building {target.BuildArchString} ...");

                    foreach (var proj in sourceProjects)
                    {
                        if (target.OS != OSType.Windows && windowsOnly.Contains(proj))
                            continue;

                        var command = new string[] {
                            "dotnet", "publish", proj,
                            "-c", "Release",
                            "-o", outputFolder,
                            "-r", target.BuildArchString,
                            $"/p:AssemblyVersion={releaseInfo.Version}",
                            $"/p:Version={releaseInfo.Version}-{releaseInfo.Type}-{releaseInfo.Timestamp:yyyyMMdd}",
                            "--self-contained", "false"
                        };
                        await ProcessHelper.ExecuteWithLog(command, workingDirectory: outputFolder, logFolder: logFolder, logFilename: (pid, isStdOut) => $"{Path.GetFileNameWithoutExtension(proj)}.{target}.{pid}.{(isStdOut ? "stdout" : "stderr")}.log");
                    }
                }

                await PrepareTargetDirectory(baseDir, outputFolder, target.OS, target.Arch, rtcfg);

                Console.WriteLine("Completed!");
            }

            Console.WriteLine("Build completed, building installers...");

        }, buildTargetOption, buildTempOption, solutionFileOption, gitStashPushOption, releaseTypeOption, updateUrlsOption, keepBuildsOption);

        return command;
    }

    /// <summary>
    /// Updates the source directory prior to building
    /// </summary>
    /// <param name="baseDir">The source folder base</param>
    /// <param name="releaseInfo">The release info to use</param>
    /// <param name="updateUrls">The urls to check for updates<param>
    /// <returns>An awaitable task</returns>
    static Task PrepareSourceDirectory(string baseDir, ReleaseInfo releaseInfo, string updateUrls)
    {
        updateUrls = updateUrls
            .Replace("${RELEASE_TYPE}", releaseInfo.Type.ToString().ToLowerInvariant())
            .Replace("${RELEASE_VERSION}", releaseInfo.Version.ToString())
            .Replace("${RELEASE_TIMESTAMP}", releaseInfo.Timestamp.ToString("yyyy-MM-dd"));

        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "License", "VersionTag.txt"), releaseInfo.Version.ToString());
        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateBuildChannel.txt"), releaseInfo.Type.ToString().ToLowerInvariant());
        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateURL.txt"), updateUrls);
        File.Copy(
            Path.Combine(baseDir, "Updates", "release_key.txt"),
            Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateSignKey.txt"),
            true
        );

        return Task.CompletedTask;
    }

    /// <summary>
    /// Prepares a target directory with fixes that are done post-build, but before making the individual packages
    /// </summary>
    /// <param name="baseDir">The source directory</param>
    /// <param name="buildDir">The output build directory to modify</param>
    /// <param name="os">The target operating system</param>
    /// <param name="arch">The target architecture</param>
    /// <param name="rtcfg">The runtime config</param>
    /// <returns>An awaitable task</returns>
    static async Task PrepareTargetDirectory(string baseDir, string buildDir, OSType os, ArchType arch, RuntimeConfig rtcfg)
    {
        await RemoveUnwantedFiles(os, buildDir);

        switch (os)
        {
            case OSType.Windows:
                await SignWindowsExecutables(buildDir, rtcfg);
                break;

            case OSType.MacOS:
                await BundleMacOSApplication(baseDir, buildDir, rtcfg);
                break;

            case OSType.Linux:
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// A list of folders that are unwanted for a given OS target
    /// </summary>
    /// <param name="os">The OS to get unwanted folders for</param>
    /// <returns>The unwanted folders</returns>
    static string[] UnwantedFolders(OSType os)
        => os switch
        {
            OSType.Windows => ["lvm-scripts"],
            OSType.MacOS => ["lvm-scripts", "win-tools"],
            OSType.Linux => ["win-tools"],
            _ => throw new Exception($"Not supported os: {os}")
        };

    /// <summary>
    /// A list of files that are unwanted for a given OS target
    /// </summary>
    /// <param name="os">The OS to get unwanted files for</param>
    /// <returns>The files that are unwanted</returns>
    static string[] UnwantedFiles(OSType os)
        => os switch
        {
            OSType.Windows => [],
            OSType.MacOS => [Path.Combine("utility-scripts", "DuplicatiVerify.ps1")],
            OSType.Linux => [Path.Combine("utility-scripts", "DuplicatiVerify.ps1")],
            _ => throw new Exception($"Not supported os: {os}")
        };


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
    /// <param name="rtcfg">The runtime configuration</param>
    /// <returns>An awaitable task</returns>
    static async Task BundleMacOSApplication(string baseDir, string buildDir, RuntimeConfig rtcfg)
    {
        var buildroot = Path.GetDirectoryName(buildDir) ?? throw new Exception("Bad build dir");
        // Create target .app folder
        var appDir = Path.Combine(
            buildroot,
            $"{Path.GetFileName(buildDir)}-{MacOSAppName}"
        );

        if (Directory.Exists(appDir))
        {
            Console.WriteLine("App folder already exsists, skipping MacOS application build");
            return;
        }

        // Prepare the .app folder structure
        var tmpApp = Path.Combine(buildroot, "tmpapp", MacOSAppName);

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
        var installerDir = Path.Combine(baseDir, "Installer", "MacOS");

        var plist = File.ReadAllText(Path.Combine(installerDir, "app-resources", "Info.plist"))
            .Replace("!LONG_VERSION!", rtcfg.ReleaseInfo.ReleaseName)
            .Replace("!SHORT_VERSION!", rtcfg.ReleaseInfo.Version.ToString());

        File.WriteAllText(
            Path.Combine(tmpApp, "Contents", "Info.plist"),
            plist
        );

        File.Copy(
            Path.Combine(installerDir, "app-resources", "Duplicati.icns"),
            Path.Combine(tmpApp, "Contents", "Resources", "Duplicati.icns"),
            overwrite: true
        );

        // Inject the launch agent
        EnvHelper.CopyDirectory(
            Path.Combine(installerDir, "daemon"),
            Path.Combine(tmpApp, "Contents", "Resources"),
            recursive: true
        );

        // Inject the uninstall.sh script
        File.Copy(
            Path.Combine(installerDir, "uninstall.sh"),
            Path.Combine(tmpApp, "Contents", "MacOS", "uninstall.sh"),
            overwrite: true
        );

        if (!OperatingSystem.IsWindows())
        {
            // Mark executables with the execute flag
            var executables = rtcfg.ExecutableBinaries.Select(x => Path.Combine(binDir, x))
                .Concat(Directory.EnumerateFiles(binDir, "*.sh", SearchOption.AllDirectories));
            var filemode = EnvHelper.GetUnixFileMode("+x");
            foreach (var x in executables)
                if (File.Exists(x))
                    EnvHelper.AddFilemode(x, filemode);
        }

        if (rtcfg.UseCodeSignSigning)
        {
            var entitlementFile = Path.Combine(installerDir, "Entitlements.plist");
            foreach (var f in Directory.EnumerateFiles(binDir, "*", SearchOption.AllDirectories))
                await rtcfg.Codesign(f, entitlementFile);

            await rtcfg.Codesign(Path.Combine(tmpApp), entitlementFile);
        }

        // foreach (var f in Directory.EnumerateFiles(binDir, "*.launchagent.plist", SearchOption.TopDirectoryOnly))
        //     File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);


        Directory.Move(tmpApp, appDir);
        Directory.Delete(Path.GetDirectoryName(tmpApp) ?? throw new Exception("Unexpected empty path"));
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
