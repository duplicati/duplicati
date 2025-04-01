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
    /// The primary project to build for CLI builds
    /// </summary>
    private const string PrimaryCLIProject = "Duplicati.CommandLine.csproj";

    /// <summary>
    /// The primary project to build for Agent builds
    /// </summary>
    private const string PrimaryAgentProject = "Duplicati.Agent.csproj";

    /// <summary>
    /// Projects that only makes sense for Windows
    /// </summary>
    private static readonly IReadOnlySet<string> WindowsOnlyProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.WindowsService.csproj" };

    /// <summary>
    /// Projects the pull in GUI dependencies
    /// </summary>
    private static readonly IReadOnlySet<string> GUIOnlyProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.GUI.TrayIcon.csproj" };

    /// <summary>
    /// Projects that are Agent only
    /// </summary>
    private static readonly IReadOnlySet<string> AgentOnlyProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "Duplicati.Agent.csproj" };

    /// <summary>
    /// The projects that are part of the agent builds
    /// </summary>
    private static readonly IReadOnlySet<string> AgentProjects = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) {
        "Duplicati.Agent.csproj",
        "Duplicati.Service.csproj",
        "Duplicati.WindowsService.csproj"
    };

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
        { "Duplicati.CommandLine.SharpAESCrypt", "duplicati-aescrypt" },
        { "Duplicati.CommandLine.Snapshots", "duplicati-snapshots" },
        { "Duplicati.CommandLine.ServerUtil", "duplicati-server-util" },
        { "Duplicati.CommandLine.SecretTool", "duplicati-secret-tool" },
        { "Duplicati.CommandLine.SyncTool", "duplicati-sync-tool" },
        { "Duplicati.CommandLine.SourceTool", "duplicati-source-tool" },
        { "Duplicati.Service", "duplicati-service" },
        { "Duplicati.Agent", "duplicati-agent" },
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
    private static readonly IReadOnlyList<string> FedoraGUIDepends = ["libICE", "libSM", "fontconfig", "libicu", "desktop-file-utils"];
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
        /// <param name="version">The version</param>
        /// <param name="increment">The build version increment</param>
        /// <returns>The release info</returns>
        public static ReleaseInfo Create(ReleaseChannel type, Version version, int increment)
            => new ReleaseInfo(new Version(version.Major, version.Minor, version.Build, version.Revision + increment), type, DateTime.Today);
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

        var signkeyPinOption = new Option<string>(
            name: "--signkey-pin",
            description: "The pin to use for the signing key",
            getDefaultValue: () => string.Empty
        );

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
            name: "--changelog-news-file",
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

        var useHostedBuildsOption = new Option<bool>(
            name: "--use-hosted-builds",
            description: "Create hosted builds that require .NET installed, instead of self-contained builds with no .NET dependency",
            getDefaultValue: () => false
        );

        var resumeFromUploadOption = new Option<bool>(
            name: "--resume-from-upload",
            description: "Resumes the build process from the upload step",
            getDefaultValue: () => false
        );

        var propagateReleaseOption = new Option<bool>(
            name: "--propagate-release",
            description: "Propagate the release to the next channel",
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
            signkeyPinOption,
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
            disableDiscordAnnounceOption,
            useHostedBuildsOption,
            resumeFromUploadOption,
            propagateReleaseOption
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
    /// <param name="SignkeyPin">The pin to use for the signing key</param>
    /// <param name="DisableDockerPush">If the docker push should be disabled</param>
    /// <param name="MacOSAppName">The name of the MacOS app bundle</param>
    /// <param name="DockerRepo">The docker repository to push to</param>
    /// <param name="ChangelogNewsFile">The path to the changelog news file</param>
    /// <param name="DisableNotarizeSigning">If notarize signing should be disabled</param>
    /// <param name="DisableGpgSigning">If GPG signing should be disabled</param>
    /// <param name="DisableS3Upload">If S3 upload should be disabled</param>
    /// <param name="DisableGithubUpload">If Github upload should be disabled</param>
    /// <param name="DisableUpdateServerReload">If the update server should not be reloaded</param>
    /// <param name="DisableDiscordAnnounce">If forum posting should be disabled</param>
    /// <param name="UseHostedBuilds">If hosted builds should be used</param>
    /// <param name="ResumeFromUpload">If the process should resume from the upload step</param>
    /// <param name="PropagateRelease">If the release should be propagated to the next channel</param>
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
        string SignkeyPin,
        bool DisableDockerPush,
        string MacOSAppName,
        string DockerRepo,
        FileInfo ChangelogNewsFile,
        bool DisableNotarizeSigning,
        bool DisableGpgSigning,
        bool DisableS3Upload,
        bool DisableGithubUpload,
        bool DisableUpdateServerReload,
        bool DisableDiscordAnnounce,
        bool UseHostedBuilds,
        bool ResumeFromUpload,
        bool PropagateRelease
    );

    static async Task DoBuild(CommandInput input)
    {
        Console.WriteLine($"Building {input.Channel} release ...");
        var configuration = Configuration.Create(input.Channel);

        var buildTargets = input.Targets;

        if (!buildTargets.Any())
            buildTargets = Program.SupportedPackageTargets.ToArray();

        if (!input.SolutionFile.Exists)
            throw new FileNotFoundException($"Solution file not found: {input.SolutionFile.FullName}");

        // This could be fixed, so we will throw an exception if the build is not possible
        if (buildTargets.Any(x => x.Package == PackageType.MSI) && !configuration.IsMSIBuildPossible())
            throw new Exception("WiX toolset not configured, cannot build MSI files");

        // This will be fixed in the future, but requires a new http-interface for Synology DSM
        if (buildTargets.Any(x => x.Package == PackageType.SynologySpk) && !configuration.IsSynologyPkgPossible())
            throw new Exception("Synology SPK files are currently not supported");

        // This will not work, so to make it easier for non-MacOS developers, we will remove the MacOS packages
        if (buildTargets.Any(x => x.Package == PackageType.MacPkg || x.Package == PackageType.DMG) && !configuration.IsMacPkgBuildPossible())
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
        var primaryAgent = sourceProjects.FirstOrDefault(x => string.Equals(Path.GetFileName(x), PrimaryAgentProject, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Failed to find agent executable");
        var windowsOnly = sourceProjects.Where(x => WindowsOnlyProjects.Contains(Path.GetFileName(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var guiOnlyProjects = sourceProjects.Where(x => GUIOnlyProjects.Contains(Path.GetFileName(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var agentOnlyProjects = sourceProjects.Where(x => AgentOnlyProjects.Contains(Path.GetFileName(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Put primary at the end
        sourceProjects.Remove(primaryGUI);
        sourceProjects.Remove(primaryCLI);
        sourceProjects.Remove(primaryAgent);
        sourceProjects.Add(primaryAgent);
        sourceProjects.Add(primaryCLI);
        sourceProjects.Add(primaryGUI);

        if (!File.Exists(primaryGUI))
            throw new Exception($"Failed to locate project file: {primaryGUI}");
        if (!File.Exists(primaryCLI))
            throw new Exception($"Failed to locate project file: {primaryCLI}");

        if (!input.ChangelogNewsFile.Exists)
        {
            Console.WriteLine($"Changelog news file not found: {input.ChangelogNewsFile.FullName}");
            Console.WriteLine($"Create an empty file if you want a release without changes");
            if (OperatingSystem.IsWindows())
                Console.WriteLine($"> type nul > {input.ChangelogNewsFile.FullName}");
            else
                Console.WriteLine($"> touch {input.ChangelogNewsFile.FullName}");

            Program.ReturnCode = 1;
            return;
        }

        if (input.ChangelogNewsFile.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-1))
        {
            Console.WriteLine($"Changelog news file was last modified {input.ChangelogNewsFile.LastAccessTimeUtc:yyyy-MM-dd}, indicating a stale file");
            Console.WriteLine($"Please update the file with the changelog news for the release");
            Program.ReturnCode = 1;
            return;
        }

        var changelogNews = File.ReadAllText(input.ChangelogNewsFile.FullName);

        var releaseInfo = string.IsNullOrWhiteSpace(input.Version)
            ? ReleaseInfo.Create(input.Channel, Version.Parse(File.ReadAllText(versionFilePath)), 1)
            : ReleaseInfo.Create(input.Channel, Version.Parse(input.Version), 0);
        Console.WriteLine($"Building {releaseInfo.ReleaseName} ...");

        var keyfilePassword = input.Password;
        if (string.IsNullOrWhiteSpace(keyfilePassword))
            keyfilePassword = EnvHelper.GetEnvKey("KEYFILE_PASSWORD", "");
        if (string.IsNullOrWhiteSpace(keyfilePassword))
            keyfilePassword = ConsoleHelper.ReadPassword("Enter keyfile password");

        var primarySignKey = LoadKeyFile(configuration.ConfigFiles.UpdaterKeyfile.FirstOrDefault(), keyfilePassword, false);
        var additionalKeys = configuration.ConfigFiles.UpdaterKeyfile
            .Skip(1)
            .Select(x => LoadKeyFile(x, keyfilePassword, true));

        // Configure runtime environment
        var rtcfg = new RuntimeConfig(
            configuration,
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

        // Prevent the system from going to sleep during the build
        using var _ = new KeepAliveAssertion();

        if (rtcfg.UseGithubUpload && !input.GitStashPush)
        {
            Console.WriteLine("Github upload requested, but pushing the tag is disabled");
            Program.ReturnCode = 1;
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
        if (input.GitStashPush && !input.ResumeFromUpload)
            await ProcessHelper.Execute(["git", "stash", "save", $"auto-build-{releaseInfo.Timestamp:yyyy-MM-dd}"], workingDirectory: baseDir);

        // Inject various files that will be embedded into the build artifacts
        var revertableFiles = await PrepareSourceDirectory(baseDir, releaseInfo, rtcfg);

        // Inject a version tag into the html files
        revertableFiles.AddRange(InjectVersionIntoFiles(baseDir, releaseInfo));

        // Record the files that are replaced by the npm package
        var foldersToRemove = new List<string>();
        revertableFiles.AddRange(await ReplaceNpmPackages(baseDir, input.BuildPath.FullName, foldersToRemove, releaseInfo, rtcfg));

        // Perform the main compilations
        var sourceBuildMap = new Dictionary<InterfaceType, IEnumerable<string>> {
            { InterfaceType.GUI, sourceProjects
                .Where(x => !AgentOnlyProjects.Contains(Path.GetFileName(x))) },
            { InterfaceType.Cli, sourceProjects
                .Where(x => !GUIOnlyProjects.Contains(Path.GetFileName(x)))
                .Where(x => !AgentOnlyProjects.Contains(Path.GetFileName(x))) },
            { InterfaceType.Agent, sourceProjects
                .Where(x => AgentProjects.Contains(Path.GetFileName(x)))
            }
        };

        await Compile.BuildProjects(baseDir, input.BuildPath.FullName, sourceBuildMap, windowsOnly, buildTargets, releaseInfo, input.KeepBuilds, rtcfg, input.UseHostedBuilds);

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
                incompatibleUpdateUrl: ReplaceVersionPlaceholders(configuration.ExtraSettings.UpdateFromIncompatibleVersionUrl, releaseInfo),
                genericUpdatePageUrl: ReplaceVersionPlaceholders(configuration.ExtraSettings.GenericUpdatePageUrl, releaseInfo),
                releaseType: releaseInfo.Channel.ToString().ToLowerInvariant(),
                packages: builtPackages.Select(x => new PackageEntry(
                    RemoteUrls: configuration.ExtraSettings.PackageUrls
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

        // Ensure the tag is pushed before uploading, so the uploaded files
        // are associated with the release tag
        if (input.GitStashPush && !input.ResumeFromUpload)
            await GitPush.TagAndPush(baseDir, releaseInfo);

        // Propagate the release to the next channel, if selected
        var nextChannels = new[] { ReleaseChannel.Experimental, ReleaseChannel.Beta, ReleaseChannel.Stable }
            .Where(x => x > releaseInfo.Channel)
            .Where(x => rtcfg.ReleaseInfo.Channel != ReleaseChannel.Canary)
            .Where(x => input.PropagateRelease)
            .ToArray();

        if (rtcfg.UseS3Upload || rtcfg.UseGithubUpload)
        {
            Console.WriteLine("Build completed, uploading packages ...");
            if (rtcfg.UseS3Upload)
            {
                var manifestNames = new[] { $"duplicati-{releaseInfo.ReleaseName}.manifest", "latest-v2.manifest" };

                var packageJson = Path.Combine(input.BuildPath.FullName, "packages", "latest-v2.json");
                var content = Upload.CreatePackageJson(builtPackages, rtcfg);

                File.WriteAllText(packageJson, content);

                var uploads = files.Select(x => new Upload.UploadFile(x, Path.GetFileName(x)))
                   .Concat(manifestNames.Select(x => new Upload.UploadFile(manifestfile, x)))
                   .Append(new Upload.UploadFile(packageJson, Path.GetFileName(packageJson)))
                   .Append(new Upload.UploadFile(packageJson, $"latest-v2-{releaseInfo.Version}.json"));

                await Upload.UploadToS3(uploads, rtcfg, nextChannels);
            }

            if (rtcfg.UseGithubUpload)
            {
                if (!rtcfg.UseS3Upload && input.GitStashPush && !input.ResumeFromUpload)
                {
                    Console.WriteLine("Waiting for Github to create the release tag ...");
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }

                await Upload.UploadToGithub(
                    files.Select(x => new Upload.UploadFile(x, Path.GetFileName(x))),
                    rtcfg
                );
            }
        }

        if (rtcfg.UseUpdateServerReload)
        {
            Console.WriteLine("Release completed, reloading update server ...");
            await Upload.ReloadUpdateServer(rtcfg, nextChannels);
        }

        if (rtcfg.UseForumPosting)
        {
            Console.WriteLine("Release completed, posting release notes ...");
            await Upload.PostToForum(rtcfg);
        }

        if (input.GitStashPush)
        {
            // Contents are added to changelog, so we can remove the file
            input.ChangelogNewsFile.Delete();
        }

        // Clean up the source tree
        foreach (var folder in foldersToRemove)
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

        // This often fails and should be fixed
        try
        {
            await ProcessHelper.Execute(new[] {
                "git", "checkout",
            }.Concat(revertableFiles.Select(x => Path.GetRelativePath(baseDir, x)))
             , workingDirectory: baseDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to clean source directory: {ex.Message}");
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

        var wixFileGUI = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "Windows", "TrayIcon", "UpgradeData.wxi");
        File.WriteAllText(
            wixFileGUI,
            Regex.Replace(
                File.ReadAllText(wixFileGUI),
                @"\<\?define ProductVersion\=\""" + versionre + @"\"" \?\>",
                $"<?define ProductVersion=\"{releaseInfo.Version}\" ?>"
            )
        );

        targetfiles.Add(wixFileGUI);

        var wixFileAgent = Path.Combine(baseDir, "ReleaseBuilder", "Resources", "Windows", "Agent", "UpgradeData.wxi");
        File.WriteAllText(
            wixFileAgent,
            Regex.Replace(
                File.ReadAllText(wixFileAgent),
                @"\<\?define ProductVersion\=\""" + versionre + @"\"" \?\>",
                $"<?define ProductVersion=\"{releaseInfo.Version}\" ?>"
            )
        );

        targetfiles.Add(wixFileAgent);

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
        var urlstring = string.Join(";", rtcfg.Configuration.ExtraSettings.UpdaterUrls.Select(x =>
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
            incompatibleUpdateUrl: ReplaceVersionPlaceholders(rtcfg.Configuration.ExtraSettings.UpdateFromIncompatibleVersionUrl, releaseInfo),
            genericUpdatePageUrl: ReplaceVersionPlaceholders(rtcfg.Configuration.ExtraSettings.GenericUpdatePageUrl, releaseInfo),
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
    /// Uses NPM to replace the node_modules folder in the webroot with a fresh install
    /// </summary>
    /// <param name="baseDir">The base directory</param>
    /// <param name="buildDir">The build directory</param>
    /// <param name="foldersToRemove">The folders to remove after the build</param>
    /// <param name="releaseInfo">The release info</param>
    /// <param name="rtcfg">The runtime configuration</param>
    /// <returns>The files that were deleted</returns>
    static async Task<List<string>> ReplaceNpmPackages(string baseDir, string buildDir, List<string> foldersToRemove, ReleaseInfo releaseInfo, RuntimeConfig rtcfg)
    {
        var deleted = new List<string>();

        // Find all webroot folders with a package.json and package-lock.json
        var webroot = Path.Combine(baseDir, "Duplicati", "Server", "webroot");
        var targets = Directory.EnumerateDirectories(webroot, "*", SearchOption.TopDirectoryOnly)
            .Where(x => File.Exists(Path.Combine(x, "package.json")) && File.Exists(Path.Combine(x, "package-lock.json")))
            .ToList();

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(rtcfg.Configuration.Commands.Npm))
                throw new Exception("NPM command not found, but required for building");

            // Remove existing node_modules folder
            if (Directory.Exists(Path.Combine(target, "node_modules")))
                Directory.Delete(Path.Combine(target, "node_modules"), true);

            // These will be deleted after the build
            deleted.AddRange(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories));

            var tmp = Path.Combine(buildDir, "tmp-npm");
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);

            Directory.CreateDirectory(tmp);
            EnvHelper.CopyDirectory(target, tmp, true);

            // Run npm install in the temporary folder
            await ProcessHelper.Execute([rtcfg.Configuration.Commands.Npm, "ci"], workingDirectory: tmp);
            var basefolder = Directory.EnumerateDirectories(Path.Combine(tmp, "node_modules"), "*", SearchOption.AllDirectories)
                .Where(x => File.Exists(Path.Combine(x, "index.html")))
                .OrderBy(x => x.Length)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(basefolder))
                throw new Exception($"Failed to locate node_modules folder in {target}");

            Directory.Delete(target, true);
            Directory.Move(basefolder, target);
            Directory.Delete(tmp, true);
            foldersToRemove.Add(target);

            var indexfile = Path.Combine(target, "index.html");
            if (File.Exists(indexfile))
            {
                var content = File.ReadAllText(indexfile);
                var prefix = target.Substring(webroot.Length).Replace(Path.DirectorySeparatorChar, '/').Trim('/');
                content = content.Replace("<base href=\"/\">", $"<base href=\"/{prefix}/\">");
                File.WriteAllText(indexfile, content);
            }
        }

        return deleted.Where(x => !Path.GetFileName(x).StartsWith("."))
            .ToList();
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
