namespace ReleaseBuilder;

using static EnvHelper;

/// <summary>
/// The release types
/// </summary>
public enum ReleaseType
{
    /// <summary>
    /// The primary release form
    /// </summary>
    Stable,
    /// <summary>
    /// Beta releases
    /// </summary>
    Beta,
    /// <summary>
    /// Experimental are slightly less unstable than canary
    /// </summary>
    Experimental,
    /// <summary>
    /// The regular releases, may have breaking changes
    /// </summary>
    Canary,
    /// <summary>
    /// Nightly, unmonitored builds
    /// </summary>
    Nightly
}

/// <summary>
/// Represents the environment configuration
/// </summary>
/// <param name="ConfigFiles">The configuration files</param>
/// <param name="Commands">The commands</param>
public record Configuration(
    ConfigFiles ConfigFiles,
    Commands Commands
)
{
    /// <summary>
    /// Creates a new <see cref="Configuration"/> 
    /// </summary>
    /// <returns>The new configuration</returns>
    public static Configuration Create()
        => new(
            ConfigFiles.Create(),
            Commands.Create()
        );

    /// <summary>
    /// Checks if signing with authenticode is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if signing is possible</returns>
    public bool IsAuthenticodePossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.AuthenticodePasswordFile) || string.IsNullOrWhiteSpace(ConfigFiles.AuthenticodePfxFile) || string.IsNullOrWhiteSpace(Commands.OsslSignCode))
            return false;

        if (!File.Exists(ConfigFiles.AuthenticodePasswordFile) || !File.Exists(ConfigFiles.AuthenticodePfxFile))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if signing with MacOS codesign is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if codesign is possible</returns>
    public bool IsCodeSignPossible()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        if (string.IsNullOrWhiteSpace(ConfigFiles.CodesignIdentity) || string.IsNullOrWhiteSpace(Commands.Codesign))
            return false;

        return true;
    }
}

/// <summary>
/// Configuration files used by the build script
/// </summary>
/// <param name="UpdaterKeyfile">The key file used to sign manifests</param>
/// <param name="GpgKeyfile">The GPG key used to build signed hash files</param>
/// <param name="AuthenticodePfxFile">The PFX file used to sign binaries</param>
/// <param name="AuthenticodePasswordFile">The encrypted file containing the password used to unlock the PFX file</param>
/// <param name="GithubTokenFile">The token used for Github uploads</param>
/// <param name="DiscourseTokenFile">The token used for Discourse forum announce</param>
/// <param name="CodesignIdentity">The identity to use for MacOS signing</param>
/// <param name="NotarizeUsername">The username for MacOS notarization</param>
/// <param name="NotarizePassword">The password for MacOS notarization</param>
public record ConfigFiles(
    string UpdaterKeyfile,
    string GpgKeyfile,
    string AuthenticodePfxFile,
    string AuthenticodePasswordFile,
    string GithubTokenFile,
    string DiscourseTokenFile,
    string CodesignIdentity,
    string NotarizeUsername,
    string NotarizePassword
)
{
    /// <summary>
    /// Generates a new config files instance
    /// </summary>
    /// <returns>The config files instance</returns>

    public static ConfigFiles Create()
    {
        var gatekeeperSettingsFile = ExpandEnv("GATEKEEPER_SETTINGS_FILE", "${HOME}/.config/signkeys/Duplicati/macos-gatekeeper");
        if (File.Exists(gatekeeperSettingsFile))
        {
            var kvp = File.ReadAllLines(gatekeeperSettingsFile)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.StartsWith("export "))
                .Select(x => x.Substring("export ".Length).Trim().Split("=", 2))
                .Where(x => x.Length == 2)
                .Select(x => new { Key = x[0], Value = x[1] });

            foreach (var k in kvp)
                Environment.SetEnvironmentVariable(k.Key, k.Value);
        }

        return new(
            ExpandEnv("UPDATER_KEYFILE", "${HOME}/.config/signkeys/Duplicati/updater-release.key"),
            ExpandEnv("GPG_KEYFILE", "${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"),
            ExpandEnv("AUTHENTICODE_PFXFILE", "${HOME}/.config/signkeys/Duplicati/authenticode.pfx"),
            ExpandEnv("AUTHENTICODE_PASSWORD", "${HOME}/.config/signkeys/Duplicati/authenticode.key"),
            ExpandEnv("GITHUB_TOKEN_FILE", "${HOME}/.config/github-api-token"),
            ExpandEnv("DISCOURSE_TOKEN_FILE", "${HOME}/.config/discourse-api-token"),
            ExpandEnv("CODESIGN_IDENTITY", ""),
            ExpandEnv("NOTARIZE_USERNAME", ""),
            ExpandEnv("NOTARIZE_PASSWORD", "@keychain:NOTARIZE_CMDLINE")
        );
    }
}

/// <summary>
/// Configuration of commands used by the build script
/// </summary>
/// <param name="Dotnet">The &quot;build&quot; command</param>
/// <param name="Gpg">The &quot;gpg&quot; command</param>
/// <param name="AwsCli">The &quot;aws&quot; command</param>
/// <param name="GithubRelease">The &quot;github-release&quot; command</param>
/// <param name="OsslSignCode">The &quot;osslsigncode&quot; command</param>
/// <param name="Codesign">The &quot;codesign&quot; command</param>
public record Commands(
    string Dotnet,
    string? Gpg,
    string? AwsCli,
    string? GithubRelease,
    string? OsslSignCode,
    string? Codesign
)
{
    /// <summary>
    /// Generates a new command instance
    /// </summary>
    /// <returns>The command instance</returns>
    public static Commands Create()
        => new(
            FindCommand("dotnet", "DOTNET") ?? throw new Exception("Failed to find the \"dotnet\" command"),
            FindCommand("gpg2", "GPG", FindCommand("gpg", "GPG")),
            FindCommand("aws", "AWSCLI"),
            FindCommand("github-release", "GITHUB_RELEASE"),
            FindCommand(OperatingSystem.IsWindows() ? "signtool.exe" : "osslsigncode", "SIGNTOOL"),
            OperatingSystem.IsMacOS() ? FindCommand("codesign", "CODESIGN") : null
        );
}

