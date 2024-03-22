using System.CommandLine;

namespace ReleaseBuilder.CliCommand;

/// <summary>
/// The build command implementation
/// </summary>
public static partial class Build
{
    /// <summary>
    /// The primary project to build for GUI builds
    /// </summary>
    private const string PrimaryGUIProject = "Duplicati.GUI.TrayIcon.csproj";

    /// <summary>
    /// The secondary project to build for CLI builds
    /// </summary>
    private const string PrimaryCLIProject = "Duplicati.CommandLine.csproj";

    /// <summary>
    /// Projects that only makes sense for Windows
    /// </summary>
    private static readonly IReadOnlySet<string> WindowsOnlyProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.WindowsService.csproj" };

    /// <summary>
    /// Projects the pull in GUI dependencies
    /// </summary>
    private static readonly IReadOnlySet<string> GUIProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.GUI.TrayIcon.csproj" };

    /// <summary>
    /// Some executables have shorter names that follow the Linux convention of all-lowercase
    /// </summary>
    private static readonly IDictionary<string, string> ExecutableRenames = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
    {
        { "Duplicati.CommandLine", "duplicati-cli" },
        { "Duplicati.Server", "duplicati-server"},
        { "Duplicati.CommandLine.BackendTester", "duplicati-backend-tester"},
        { "Duplicati.CommandLine.BackendTool", "duplicati-backend-tool" },
        { "Duplicati.CommandLine.RecoveryTool", "duplicati-recovery-tool" },
        { "Duplicati.GUI.TrayIcon", "duplicati" }
    };

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
        /// Performs codesign on the given file
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

        /// <summary>
        /// Performs productsign on the given file
        /// </summary>
        /// <param name="file">The file to sign</param>
        /// <returns>An awaitable task</returns>
        public Task Productsign(string file)
            => UseCodeSignSigning
                ? ProcessRunner.MacOSProductSign(
                    Program.Configuration.Commands.Productsign!,
                    Program.Configuration.ConfigFiles.CodesignIdentity,
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

            // This could be fixed, so we will throw an exception if the build is not possible
            if (buildTargets.Any(x => x.Package == PackageType.MSI) && !Program.Configuration.IsMSIBuildPossible())
                throw new Exception("WiX toolset not configured, cannot build MSI files");

            // This will be fixed in the future, but requires a new http-interface for Synology DSM
            if (buildTargets.Any(x => x.Package == PackageType.SynologySpk) && !Program.Configuration.IsSynologyPkgPossible())
                throw new Exception("Synology SPK files are currently not supported");

            // This will not work, so to make it easier for non-MacOS developers, we will remove the MacOS packages
            if (buildTargets.Any(x => x.Package == PackageType.MacPkg || x.Package == PackageType.DMG) && !Program.Configuration.IsMacPkgBuildPossible())
            {
                Console.WriteLine("MacOS packages requested but not running on MacOS, removing from build targets");
                buildTargets = buildTargets.Where(x => x.Package != PackageType.MacPkg && x.Package != PackageType.DMG).ToArray();
            }

            var baseDir = Path.GetDirectoryName(solutionFile.FullName) ?? throw new Exception("Path to solution file was invalid");
            var versionFilePath = Path.Combine(baseDir, "Updates", "build_version.txt");
            if (!File.Exists(versionFilePath))
                throw new FileNotFoundException($"Version file not found: {versionFilePath}");

            var sourceProjects = Directory.EnumerateDirectories(Path.Combine(baseDir, "Executables", "net8"), "*", SearchOption.TopDirectoryOnly)
                .SelectMany(x => Directory.EnumerateFiles(x, "*.csproj", SearchOption.TopDirectoryOnly))
                .ToList();

            var primaryGUI = sourceProjects.FirstOrDefault(x => string.Equals(Path.GetFileName(x), PrimaryGUIProject, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Failed to find tray icon executable");
            var primaryCLI = sourceProjects.FirstOrDefault(x => string.Equals(Path.GetFileName(x), PrimaryCLIProject, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Failed to find cli executable");
            var windowsOnly = sourceProjects.Where(x => WindowsOnlyProjects.Contains(Path.GetFileName(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Put primary at the end
            sourceProjects.Remove(primaryGUI);
            sourceProjects.Remove(primaryCLI);
            sourceProjects.Add(primaryCLI);
            sourceProjects.Add(primaryGUI);

            if (!File.Exists(primaryGUI))
                throw new Exception($"Failed to locate project file: {primaryGUI}");
            if (!File.Exists(primaryCLI))
                throw new Exception($"Failed to locate project file: {primaryCLI}");

            var releaseInfo = ReleaseInfo.Create(releaseType, int.Parse(File.ReadAllText(versionFilePath)) + 1);
            Console.WriteLine($"Building {releaseInfo.ReleaseName} ...");

            var keyfilePassword = ConsoleHelper.ReadPassword("Enter keyfile password");

            // Configure runtime environment
            var rtcfg = new RuntimeConfig(releaseInfo, keyfilePassword, sourceProjects.Select(x => Path.GetFileNameWithoutExtension(x)).ToList());
            rtcfg.ToggleAuthenticodeSigning();
            rtcfg.ToggleSignCodeSigning();

            if (!keepBuilds && Directory.Exists(buildTemp.FullName))
            {
                Console.WriteLine($"Deleting build folder: {buildTemp.FullName}");
                Directory.Delete(buildTemp.FullName, true);
            }

            if (!Directory.Exists(buildTemp.FullName))
                Directory.CreateDirectory(buildTemp.FullName);

            // Generally, the builds should happen with a clean source tree, 
            // but this can be disabled for debugging
            if (gitStashPush)
                await ProcessHelper.Execute(new[] { "git", "stash", "save", $"auto-build-{releaseInfo.Timestamp:yyyy-MM-dd}" }, workingDirectory: baseDir);

            // Inject various files that will be embedded into the build artifacts
            await PrepareSourceDirectory(baseDir, releaseInfo, updateUrls);

            // Perform the main compilations
            await Compile.BuildProjects(baseDir, buildTemp.FullName, sourceProjects, windowsOnly, GUIProjects, buildTargets, releaseInfo, keepBuilds, rtcfg);

            // Create the packages
            await CreatePackage.BuildPackages(baseDir, buildTemp.FullName, buildTargets, keepBuilds, rtcfg);

            Console.WriteLine("Build completed, uploading packages ...");

            Console.WriteLine("Upload completed, releasing packages ...");

            Console.WriteLine("Release completed, posting release notes ...");

            // Clean up the source tree
            await ProcessHelper.Execute(new[] {
                    "git", "checkout",
                    "Duplicati/License/VersionTag.txt",
                    "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt",
                    "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt",
                    "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
                }, workingDirectory: baseDir);

            if (gitStashPush)
                await GitPush.TagAndPush(baseDir, releaseInfo);

            Console.WriteLine("All done");

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
}
