using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Duplicati.Library.AutoUpdater;

namespace ReleaseBuilder.Build;

/// <summary>
/// The build command implementation
/// </summary>
public static partial class Command
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
    /// <remarks>Note that the values here mirror the values in the AutoUpdater.PackageHelper, so changes should be coordinated between the two</remarks>
    private static readonly IDictionary<string, string> ExecutableRenames = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
    {
        { "Duplicati.CommandLine.BackendTester", "duplicati-backend-tester"},
        { "Duplicati.CommandLine.BackendTool", "duplicati-backend-tool" },
        { "Duplicati.CommandLine.RecoveryTool", "duplicati-recovery-tool" },
        { "Duplicati.CommandLine.AutoUpdater", "duplicati-autoupdater" },
        { "Duplicati.CommandLine.ConfigurationImporter", "duplicati-configuration-importer" },
        { "Duplicati.CommandLine", "duplicati-cli" },
        { "Duplicati.Server", "duplicati-server"},
        { "Duplicati.GUI.TrayIcon", "duplicati" }
    };

    /// <summary>
    /// The supported versions of libicu for Debian
    /// </summary>
    private static string DebianLibIcuVersions => "libicu | " + string.Join(" | ",
        "74;72;71;70;69;68;67;66;65;63;60;57;55;52"
        .Split(";", StringSplitOptions.RemoveEmptyEntries)
        .Select(x => $"libicu{x}"));

    /// <summary>
    /// The supported versions of libssl for Debian
    /// </summary>
    private static string DebianLibSslVersions => "libssl3 | libssl1.1";

    /// <summary>
    /// The packages that are required for GUI builds
    /// </summary>
    private static readonly IReadOnlyList<string> DebianGUIDepends = ["libice6", "libsm6", "libfontconfig1", DebianLibIcuVersions, DebianLibSslVersions];
    /// <summary>
    /// The packages that are required for CLI builds
    /// </summary>
    private static readonly IReadOnlyList<string> DebianCLIDepends = [DebianLibIcuVersions, DebianLibSslVersions];

    /// <summary>
    /// The packages that are required for GUI builds
    /// </summary>
    private static readonly IReadOnlyList<string> FedoraGUIDepends = ["libICE", "libSM", "fontconfig", "libicu"];
    /// <summary>
    /// The packages that are required for CLI builds
    /// </summary>
    private static readonly IReadOnlyList<string> FedoraCLIDepends = ["libicu"];


    /// <summary>
    /// Structure for keeping all variables for a single release
    /// </summary>
    /// <param name="Version">The version to use</param>
    /// <param name="Channel">The release channel</param>
    /// <param name="Timestamp">The release timestamp</param>
    private record ReleaseInfo(Version Version, ReleaseChannel Channel, DateTime Timestamp)
    {
        /// <summary>
        /// Gets the string name for the release
        /// </summary>
        public string ReleaseName => $"{Version}_{Channel.ToString().ToLowerInvariant()}_{Timestamp:yyy-MM-dd}";


        /// <summary>
        /// Create a new release info
        /// </summary>
        /// <param name="type">The release type</param>
        /// <param name="incVersion">The incremental version</param>
        /// <returns>The release info</returns>
        public static ReleaseInfo Create(ReleaseChannel type, int incVersion)
            => new ReleaseInfo(new Version(2, 0, 0, incVersion), type, DateTime.Today);

        /// <summary>
        /// Create a new release info
        /// </summary>
        /// <param name="type">The release type</param>
        /// <param name="version">The version</param>
        /// <returns>The release info</returns>
        public static ReleaseInfo Create(ReleaseChannel type, Version version)
            => new ReleaseInfo(version, type, DateTime.Today);
    }

    /// <summary>
    /// Creates the build command
    /// </summary>
    /// <returns>The command</returns>
    public static System.CommandLine.Command Create()
    {
        var releaseChannelArgument = new Argument<ReleaseChannel>(
            name: "channel",
            description: "The release channel",
            getDefaultValue: () => ReleaseChannel.Canary
        );

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

        var gitStashPushOption = new Option<bool>(
            name: "--git-stash-push",
            description: "Performs a git stash command before running the build, and a git commit after updating files",
            getDefaultValue: () => true
        );

        var keepBuildsOption = new Option<bool>(
            name: "--keep-builds",
            description: "Do not delete the build folders if they already exist (re-use build)",
            getDefaultValue: () => false
        );

        var buildOnlyOption = new Option<bool>(
            name: "--build-only",
            description: "Only build the binaries, do not create packages",
            getDefaultValue: () => false
        );

        var buildTempOption = new Option<DirectoryInfo>(
            name: "--build-path",
            description: "The path to the temporary folder used for builds; will be deleted on startup",
            getDefaultValue: () => new DirectoryInfo(Path.GetFullPath("build-temp"))
        );

        var solutionFileOption = new Option<FileInfo>(
            name: "--solution-file",
            description: "Path to the Duplicati.sln file",
            getDefaultValue: () => new FileInfo(Path.GetFullPath(Path.Combine("..", "Duplicati.sln")))
        );

        var disableAuthenticodeOption = new Option<bool>(
            name: "--disable-authenticode",
            description: "Disables authenticode signing",
            getDefaultValue: () => false
        );

        var disableCodeSignOption = new Option<bool>(
            name: "--disable-signcode",
            description: "Disables Apple signcode signing",
            getDefaultValue: () => false
        );

        var passwordOption = SharedOptions.passwordOption;

        var disableDockerPushOption = new Option<bool>(
            name: "--disable-docker-push",
            description: "Disables pushing the docker image to the repository",
            getDefaultValue: () => false
        );

        var macOsAppNameOption = new Option<string>(
            name: "--macos-app-name",
            description: "The name of the MacOS app bundle",
            getDefaultValue: () => "Duplicati.app"
        );

        var dockerRepoOption = new Option<string>(
            name: "--docker-repo",
            description: "The docker repository to push to",
            getDefaultValue: () => "duplicati/duplicati"
        );

        var changelogFileOption = new Option<FileInfo>(
            name: "--changelog-file",
            description: "The path to the changelog news file. Contents from this file are prepended to the changelog.",
            getDefaultValue: () => new FileInfo(Path.GetFullPath("changelog-news.txt"))
        );

        var disableNotarizeSigningOption = new Option<bool>(
            name: "--disable-notarize-signing",
            description: "Disables notarize signing for MacOS packages",
            getDefaultValue: () => false
        );

        var disableGpgSigningOption = new Option<bool>(
            name: "--disable-gpg-signing",
            description: "Disables GPG signing of packages",
            getDefaultValue: () => false
        );

        var versionOverrideOption = new Option<string?>(
            name: "--version",
            description: "Sets a custom version to use",
            getDefaultValue: () => null
        );

        var disableS3UploadOption = new Option<bool>(
            name: "--disable-s3-upload",
            description: "Disables uploading to S3",
            getDefaultValue: () => false
        );

        var disableGithubUploadOption = new Option<bool>(
            name: "--disable-github-upload",
            description: "Disables uploading to Github",
            getDefaultValue: () => false
        );

        var disableUpdateServerReloadOption = new Option<bool>(
            name: "--disable-update-server-reload",
            description: "Disables reloading the update server",
            getDefaultValue: () => false
        );

        var disableDiscordAnnounceOption = new Option<bool>(
            name: "--disable-discourse-announce",
            description: "Disables posting to the forum",
            getDefaultValue: () => false
        );

        var command = new System.CommandLine.Command("build", "Builds the packages for a release") {
            gitStashPushOption,
            releaseChannelArgument,
            buildTempOption,
            buildTargetOption,
            solutionFileOption,
            keepBuildsOption,
            buildOnlyOption,
            disableAuthenticodeOption,
            disableCodeSignOption,
            passwordOption,
            macOsAppNameOption,
            disableDockerPushOption,
            dockerRepoOption,
            changelogFileOption,
            disableNotarizeSigningOption,
            disableGpgSigningOption,
            versionOverrideOption,
            disableS3UploadOption,
            disableGithubUploadOption,
            disableUpdateServerReloadOption,
            disableDiscordAnnounceOption
        };

        command.Handler = CommandHandler.Create<CommandInput>(DoBuild);
        return command;
    }

    /// <summary>
    /// The input for the build command
    /// </summary>
    /// <param name="Targets">The build targets</param>
    /// <param name="BuildPath">The build path</param>
    /// <param name="SolutionFile">The solution path</param>
    /// <param name="GitStashPush">If the git stash should be performed</param>
    /// <param name="Channel">The release channel</param>
    /// <param name="Version">The version override to use</param>
    /// <param name="KeepBuilds">If the builds should be kept</param>
    /// <param name="BuildOnly">If only the build should be performed</param>
    /// <param name="DisableAuthenticode">If authenticode signing should be disabled</param>
    /// <param name="DisableSignCode">If signcode should be disabled</param>
    /// <param name="Password">The password to use for the keyfile</param>
    /// <param name="DisableDockerPush">If the docker push should be disabled</param>
    /// <param name="MacOSAppName">The name of the MacOS app bundle</param>
    /// <param name="DockerRepo">The docker repository to push to</param>
    /// <param name="ChangelogFile">The path to the changelog file</param>
    /// <param name="DisableNotarizeSigning">If notarize signing should be disabled</param>
    /// <param name="DisableGpgSigning">If GPG signing should be disabled</param>
    /// <param name="DisableS3Upload">If S3 upload should be disabled</param>
    /// <param name="DisableGithubUpload">If Github upload should be disabled</param>
    /// <param name="DisableUpdateServerReload">If the update server should not be reloaded</param>
    /// <param name="DisableDiscordAnnounce">If forum posting should be disabled</param>
    record CommandInput(
        PackageTarget[] Targets,
        DirectoryInfo BuildPath,
        FileInfo SolutionFile,
        bool GitStashPush,
        ReleaseChannel Channel,
        string? Version,
        bool KeepBuilds,
        bool BuildOnly,
        bool DisableAuthenticode,
        bool DisableSignCode,
        string Password,
        bool DisableDockerPush,
        string MacOSAppName,
        string DockerRepo,
        FileInfo ChangelogFile,
        bool DisableNotarizeSigning,
        bool DisableGpgSigning,
        bool DisableS3Upload,
        bool DisableGithubUpload,
        bool DisableUpdateServerReload,
        bool DisableDiscordAnnounce
    );

    static async Task DoBuild(CommandInput input)
    {
        Console.WriteLine($"Building {input.Channel} release ...");

        var buildTargets = input.Targets;

        if (!buildTargets.Any())
            buildTargets = Program.SupportedPackageTargets.ToArray();

        if (!input.SolutionFile.Exists)
            throw new FileNotFoundException($"Solution file not found: {input.SolutionFile.FullName}");

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

        var baseDir = Path.GetDirectoryName(input.SolutionFile.FullName) ?? throw new Exception("Path to solution file was invalid");
        var versionFilePath = Path.Combine(baseDir, "ReleaseBuilder", "build_version.txt");
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

        if (!input.ChangelogFile.Exists)
        {
            Console.WriteLine($"Changelog news file not found: {input.ChangelogFile.FullName}");
            Console.WriteLine($"Create an empty file if you want a release without changes");
            if (OperatingSystem.IsWindows())
                Console.WriteLine($"> type nul > {input.ChangelogFile.FullName}");
            else
                Console.WriteLine($"> touch {input.ChangelogFile.FullName}");

            Program.ReturnCode = 1;
            return;
        }

        if (input.ChangelogFile.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-1))
        {
            Console.WriteLine($"Changelog news file was last modified {input.ChangelogFile.LastAccessTimeUtc:yyyy-MM-dd}, indicating a stale file");
            Console.WriteLine($"Please update the file with the changelog news for the release");
            Program.ReturnCode = 1;
            return;
        }

        var changelogNews = File.ReadAllText(input.ChangelogFile.FullName);

        var releaseInfo = string.IsNullOrWhiteSpace(input.Version)
            ? ReleaseInfo.Create(input.Channel, int.Parse(File.ReadAllText(versionFilePath)) + 1)
            : ReleaseInfo.Create(input.Channel, Version.Parse(input.Version));
        Console.WriteLine($"Building {releaseInfo.ReleaseName} ...");

        var keyfilePassword = input.Password;
        if (string.IsNullOrWhiteSpace(keyfilePassword))
            keyfilePassword = EnvHelper.GetEnvKey("KEYFILE_PASSWORD", "");
        if (string.IsNullOrWhiteSpace(keyfilePassword))
            keyfilePassword = ConsoleHelper.ReadPassword("Enter keyfile password");

        var primarySignKey = LoadKeyFile(Program.Configuration.ConfigFiles.UpdaterKeyfile.FirstOrDefault(), keyfilePassword, false);
        var additionalKeys = Program.Configuration.ConfigFiles.UpdaterKeyfile
            .Skip(1)
            .Select(x => LoadKeyFile(x, keyfilePassword, true));

        // Configure runtime environment
        var rtcfg = new RuntimeConfig(
            releaseInfo,
            additionalKeys.Prepend(primarySignKey).ToList(),
            keyfilePassword,
            changelogNews,
            input);

        rtcfg.ToggleAuthenticodeSigning();
        rtcfg.ToggleSignCodeSigning();
        rtcfg.ToggleNotarizeSigning();
        rtcfg.ToggleGpgSigning();
        rtcfg.ToggleS3Upload();
        rtcfg.ToggleGithubUpload(rtcfg.ReleaseInfo.Channel);
        rtcfg.ToogleDiscourseAnnounce(rtcfg.ReleaseInfo.Channel);
        rtcfg.ToggleUpdateServerReload();
        await rtcfg.ToggleDockerBuild();

        if (!rtcfg.UseDockerBuild)
        {
            var unsupportedBuilds = buildTargets.Where(x => x.Package == PackageType.Docker || x.Package == PackageType.Deb || x.Package == PackageType.RPM).ToList();
            if (unsupportedBuilds.Any())
                throw new Exception($"The following packages cannot be built without Docker: {string.Join(", ", unsupportedBuilds.Select(x => x.PackageTargetString))}");
        }

        if (!input.KeepBuilds && Directory.Exists(input.BuildPath.FullName))
        {
            Console.WriteLine($"Deleting build folder: {input.BuildPath.FullName}");
            Directory.Delete(input.BuildPath.FullName, true);
        }

        if (!Directory.Exists(input.BuildPath.FullName))
            Directory.CreateDirectory(input.BuildPath.FullName);

        // Generally, the builds should happen with a clean source tree, 
        // but this can be disabled for debugging
        if (input.GitStashPush)
            await ProcessHelper.Execute(["git", "stash", "save", $"auto-build-{releaseInfo.Timestamp:yyyy-MM-dd}"], workingDirectory: baseDir);

        // Inject various files that will be embedded into the build artifacts
        var revertableFiles = await PrepareSourceDirectory(baseDir, releaseInfo, rtcfg);

        // Inject a version tag into the html files
        revertableFiles.AddRange(InjectVersionIntoFiles(baseDir, releaseInfo));

        // Perform the main compilations
        await Compile.BuildProjects(baseDir, input.BuildPath.FullName, sourceProjects, windowsOnly, GUIProjects, buildTargets, releaseInfo, input.KeepBuilds, rtcfg);

        if (input.BuildOnly)
        {
            Console.WriteLine("Build completed, skipping package creation ...");
            return;
        }

        // Create the packages
        var builtPackages = await CreatePackage.BuildPackages(baseDir, input.BuildPath.FullName, buildTargets, input.KeepBuilds, rtcfg);
        var files = builtPackages.Select(x => x.CreatedFile).ToList();


        // Build the signed manifest to be uploaded to remote storage
        var manifestfile = Path.Combine(input.BuildPath.FullName, "packages", "autoupdate.manifest");
        if (File.Exists(manifestfile) && new FileInfo(manifestfile).LastWriteTimeUtc > files.Max(x => new FileInfo(x).LastWriteTimeUtc))
        {
            Console.WriteLine("Manifest file already exists, skipping ...");
        }
        else
        {
            Console.WriteLine("Build completed, creating signed manifest ...");
            if (File.Exists(manifestfile))
                File.Delete(manifestfile);

            UpdaterManager.CreateSignedManifest(
                rtcfg.SignKeys,
                JsonSerializer.Serialize(new
                {
                    Displayname = $"Duplicati v{releaseInfo.Version} - {releaseInfo.Channel}",
                    ChangeInfo = rtcfg.ChangelogNews
                }),
                Path.Combine(input.BuildPath.FullName, "packages"),
                version: releaseInfo.Version.ToString(),
                incompatibleUpdateUrl: ReplaceVersionPlaceholders(Program.Configuration.ExtraSettings.UpdateFromIncompatibleVersionUrl, releaseInfo),
                genericUpdatePageUrl: ReplaceVersionPlaceholders(Program.Configuration.ExtraSettings.GenericUpdatePageUrl, releaseInfo),
                releaseType: releaseInfo.Channel.ToString().ToLowerInvariant(),
                packages: builtPackages.Select(x => new PackageEntry(
                    RemoteUrls: Program.Configuration.ExtraSettings.PackageUrls
                        .Select(u =>
                            ReplaceVersionPlaceholders(u, releaseInfo)
                            .Replace("${FILENAME}", HttpUtility.UrlEncode(Path.GetFileName(x.CreatedFile)))
                        ).ToArray(),
                    PackageTypeId: x.Target.PackageTargetString,
                    Length: new FileInfo(Path.Combine(input.BuildPath.FullName, x.CreatedFile)).Length,
                    MD5: CalculateHash(Path.Combine(input.BuildPath.FullName, x.CreatedFile), "md5"),
                    SHA256: CalculateHash(Path.Combine(input.BuildPath.FullName, x.CreatedFile), "sha256")
                ))
            );
        }

        // Create the GPG signatures for the files
        if (rtcfg.UseGPGSigning)
        {
            var sigfile = Path.Combine(input.BuildPath.FullName, "packages", $"duplicati-{releaseInfo.ReleaseName}.signatures.zip");
            if (File.Exists(sigfile) && new FileInfo(sigfile).LastWriteTimeUtc > files.Max(x => new FileInfo(x).LastWriteTimeUtc))
            {
                Console.WriteLine("Signature file already exists, skipping ...");
            }
            else
            {
                Console.WriteLine("Creating GPG signatures ...");
                await GpgSign.SignReleaseFiles(files, sigfile, rtcfg);
            }

            files.Add(sigfile);
        }

        if (rtcfg.UseS3Upload || rtcfg.UseGithubUpload)
        {
            Console.WriteLine("Build completed, uploading packages ...");
            if (rtcfg.UseS3Upload)
            {
                var manifestNames = new[] { $"duplicati-{releaseInfo.ReleaseName}.manifest", "latest-v2.manifest" };

                var packageJson = Path.Combine(input.BuildPath.FullName, "packages", "latest-v2.json");
                var packageJs = Path.Combine(input.BuildPath.FullName, "packages", "latest-v2.js");
                var content = Upload.CreatePackageJson(builtPackages, rtcfg);

                File.WriteAllText(packageJson, content);
                File.WriteAllText(packageJs, $"duplicati_installers = {content};");

                var uploads = files.Select(x => new Upload.UploadFile(x, Path.GetFileName(x)))
                   .Concat(manifestNames.Select(x => new Upload.UploadFile(manifestfile, x)))
                   .Append(new Upload.UploadFile(packageJson, Path.GetFileName(packageJson)))
                   .Append(new Upload.UploadFile(packageJs, Path.GetFileName(packageJs)))
                   .Append(new Upload.UploadFile(packageJson, $"latest-v2-{releaseInfo.Version}.json"))
                   .Append(new Upload.UploadFile(packageJs, $"latest-v2-{releaseInfo.Version}.js"));

                await Upload.UploadToS3(uploads, rtcfg);
            }

            if (rtcfg.UseGithubUpload)
                await Upload.UploadToGithub(
                    files.Select(x => new Upload.UploadFile(x, Path.GetFileName(x))),
                    rtcfg
                );
        }

        if (rtcfg.UseUpdateServerReload)
        {
            Console.WriteLine("Release completed, reloading update server ...");
            await Upload.ReloadUpdateServer(rtcfg);
        }

        if (rtcfg.UseForumPosting)
        {
            Console.WriteLine("Release completed, posting release notes ...");
            await Upload.PostToForum(rtcfg);
        }

        // Clean up the source tree
        await ProcessHelper.Execute(new[] {
                "git", "checkout",
            }.Concat(revertableFiles.Select(x => Path.GetRelativePath(baseDir, x)))
         , workingDirectory: baseDir);

        if (input.GitStashPush)
        {
            await GitPush.TagAndPush(baseDir, releaseInfo);

            // Contents are added to changelog, so we can remove the file
            input.ChangelogFile.Delete();
        }

        Console.WriteLine("All done!");
    }

    /// <summary>
    /// Replaces common placeholders with their respective values
    /// </summary>
    /// <param name="input">The string to update</param>
    /// <param name="releaseInfo">The release info to replace with</param>
    /// <param name="urlEncode">If the values should be URL encoded</param>
    /// <returns>The updated string</returns>
    private static string ReplaceVersionPlaceholders(string input, ReleaseInfo releaseInfo, bool urlEncode = true)
    {
        return input
            .Replace("${RELEASE_VERSION}", urlEncode ? HttpUtility.UrlEncode(releaseInfo.Version.ToString()) : releaseInfo.Version.ToString())
            .Replace("${RELEASE_TIMESTAMP}", urlEncode ? HttpUtility.UrlEncode(releaseInfo.Timestamp.ToString("yyyy-MM-dd")) : releaseInfo.Timestamp.ToString("yyyy-MM-dd"))
            .Replace("${RELEASE_CHANNEL}", urlEncode ? HttpUtility.UrlEncode(releaseInfo.Channel.ToString().ToLowerInvariant()) : releaseInfo.Channel.ToString().ToLowerInvariant())
            .Replace("${RELEASE_TYPE}", urlEncode ? HttpUtility.UrlEncode(releaseInfo.Channel.ToString().ToLowerInvariant()) : releaseInfo.Channel.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Injects the version number into some html files
    /// </summary>
    /// <param name="baseDir">The base directory</param>
    /// <param name="releaseInfo">The release info</param>
    /// <returns>The paths that were modified</returns>
    private static IEnumerable<string> InjectVersionIntoFiles(string baseDir, ReleaseInfo releaseInfo)
    {
        var targetfiles = Directory.EnumerateFiles(Path.Combine(baseDir, "Duplicati", "Server", "webroot"), "*", SearchOption.AllDirectories)
            .Where(x => x.EndsWith(".html") || x.EndsWith(".js"))
            .ToList();

        var versionre = @"(?<version>\d+\.\d+\.(\*|(\d+(\.(\*|\d+)))?))";
        var regex = new Regex(@"\?v\=" + versionre);
        foreach (var file in targetfiles)
            File.WriteAllText(
                file,
                regex.Replace(File.ReadAllText(file), $"?v={releaseInfo.Version}")
            );

        var wixFile = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "Windows", "UpgradeData.wxi");
        File.WriteAllText(
            wixFile,
            Regex.Replace(
                File.ReadAllText(wixFile),
                @"\<\?define ProductVersion\=\""" + versionre + @"\"" \?\>",
                $"<?define ProductVersion=\"{releaseInfo.Version}\" ?>"
            )
        );

        targetfiles.Add(wixFile);

        return targetfiles;
    }

    /// <summary>
    /// Updates the source directory prior to building.
    /// This writes a stub package manifest inside the source folder,
    /// which will be embedded in the excutable to indicate which version it was built from.
    /// </summary>
    /// <param name="baseDir">The source folder base</param>
    /// <param name="releaseInfo">The release info to use</param>
    /// <param name="rtcfg">The runtime configuration</param>
    /// <returns>Modified files</returns>
    static Task<List<string>> PrepareSourceDirectory(string baseDir, ReleaseInfo releaseInfo, RuntimeConfig rtcfg)
    {
        var urlstring = string.Join(";", Program.Configuration.ExtraSettings.UpdaterUrls.Select(x =>
            ReplaceVersionPlaceholders(x, releaseInfo)
            .Replace("${FILENAME}", HttpUtility.UrlEncode("latest-v2.manifest"))
        ));

        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "License", "VersionTag.txt"), releaseInfo.ReleaseName);
        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateBuildChannel.txt"), releaseInfo.Channel.ToString().ToLowerInvariant());
        File.WriteAllText(Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateURL.txt"), urlstring);
        File.WriteAllLines(Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateSignKeys.txt"), rtcfg.SignKeys.Select(x => x.ToXmlString(false)));

        if (!string.IsNullOrWhiteSpace(rtcfg.ChangelogNews))
        {
            var current = File.ReadAllText(Path.Combine(baseDir, "changelog.txt"));
            var previousEntry = current.IndexOf(rtcfg.ChangelogNews);
            if (previousEntry >= 0 && previousEntry < 200)
            {
                Console.WriteLine("Note: release already in changelog, skipping ...");
            }
            else
            {
                File.WriteAllText(
                    Path.Combine(baseDir, "changelog.txt"),

                    string.Join(Environment.NewLine, [
                        $"{rtcfg.ReleaseInfo.Timestamp:yyy-MM-dd} - {rtcfg.ReleaseInfo.ReleaseName}",
                        "==========",
                        rtcfg.ChangelogNews.Trim(),
                        "",
                        current
                    ])
                );
            }
        }

        // Previous versions used to install the assembly redirects after the build
        // but it looks like .Net now handles this automatically
        // If not, we need to adjust the .csproj files before building
        // find "${UPDATE_SOURCE}" - type f - name Duplicati.*.exe - maxdepth 1 - exec cp Installer/ AssemblyRedirects.xml { }.config \;

        var manifestFile = Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "autoupdate.manifest");
        if (File.Exists(manifestFile))
            File.Delete(manifestFile);

        UpdaterManager.CreateSignedManifest(
            rtcfg.SignKeys,
            JsonSerializer.Serialize(new { Displayname = $"Duplicati v{releaseInfo.Version} - {releaseInfo.Channel}" }),
            Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater"),
            version: releaseInfo.Version.ToString(),
            incompatibleUpdateUrl: ReplaceVersionPlaceholders(Program.Configuration.ExtraSettings.UpdateFromIncompatibleVersionUrl, releaseInfo),
            genericUpdatePageUrl: ReplaceVersionPlaceholders(Program.Configuration.ExtraSettings.GenericUpdatePageUrl, releaseInfo),
            releaseType: releaseInfo.Channel.ToString().ToLowerInvariant()
        );

        return Task.FromResult(new List<string> {
            manifestFile,
            Path.Combine(baseDir, "Duplicati", "License", "VersionTag.txt"),
            Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateBuildChannel.txt"),
            Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateURL.txt"),
            Path.Combine(baseDir, "Duplicati", "Library", "AutoUpdater", "AutoUpdateSignKeys.txt"),
            Path.Combine(baseDir, "changelog.txt")
        });
    }

    /// <summary>
    /// Loads a keyfile and decrypts the RSA key inside
    /// </summary>
    /// <param name="keyfile">The keyfile to decrypt</param>
    /// <param name="password">The keyfile password</param>
    /// <param name="askForNewPassword">Allow asking for a new password if the password did not match</param>
    /// <returns>The matching key</returns>
    static RSA LoadKeyFile(string? keyfile, string password, bool askForNewPassword)
    {
        if (string.IsNullOrWhiteSpace(keyfile))
            throw new Exception("Unable to load keyfile, no keyfile specified");

        if (!File.Exists(keyfile))
            throw new FileNotFoundException($"Keyfile not found: {keyfile}");

        try
        {
            using var ms = new MemoryStream();
            using var fs = File.OpenRead(keyfile);
            SharpAESCrypt.SharpAESCrypt.Decrypt(password, fs, ms);

            var rsa = RSA.Create();
            rsa.FromXmlString(System.Text.Encoding.UTF8.GetString(ms.ToArray()));

            return rsa;
        }
        catch (SharpAESCrypt.SharpAESCrypt.WrongPasswordException)
        {
            if (!askForNewPassword)
                throw;
        }

        password = ConsoleHelper.ReadPassword($"Enter password for {keyfile}");
        return LoadKeyFile(keyfile, password, false);
    }

    /// <summary>
    /// Calculates the hash of a file and returns the hash as a base64 string
    /// </summary>
    /// <param name="file">The file to calculate the has for</param>
    /// <param name="algorithm">The hash algorithm</param>
    /// <returns>The base64 encoded hash</returns>
    static string CalculateHash(string file, string algorithm)
    {
        using var fs = File.OpenRead(file);
        using var hash = algorithm.ToLowerInvariant() switch
        {
            "md5" => (HashAlgorithm)MD5.Create(),
            "sha256" => SHA256.Create(),
            _ => throw new Exception($"Unknown hash algorithm: {algorithm}")
        };

        return Convert.ToBase64String(hash.ComputeHash(fs));
    }
}
