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
namespace ReleaseBuilder;

using static EnvHelper;

/// <summary>
/// The release channels
/// </summary>
public enum ReleaseChannel
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
    Nightly,
    /// <summary>
    /// Debug builds
    /// </summary>
    Debug
}

/// <summary>
/// Represents the environment configuration
/// </summary>
/// <param name="ConfigFiles">The configuration files</param>
/// <param name="Commands">The commands</param>
/// <param name="ExtraSettings">Extra settings</param>
public record Configuration(
    ConfigFiles ConfigFiles,
    Commands Commands,
    ExtraSettings ExtraSettings
)
{
    /// <summary>
    /// Creates a new <see cref="Configuration"/> 
    /// </summary>
    /// <param name="channel">The release channel</param>
    /// <returns>The new configuration</returns>
    public static Configuration Create(ReleaseChannel channel)
        => new(
            ConfigFiles.Create(channel),
            Commands.Create(),
            ExtraSettings.Create()
        );


    /// <summary>
    /// Checks if signing with authenticode using jsign is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if signing is possible</returns>
    public bool IsAuthenticodePossibleWithJsignTool()
    {
        if (string.IsNullOrWhiteSpace(Commands.JSign))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if signing with authenticode is possible with signtool given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if signing is possible</returns>
    public bool IsAuthenticodePossibleWithSignTool()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.AuthenticodePasswordFile) || string.IsNullOrWhiteSpace(ConfigFiles.AuthenticodePfxFile) || string.IsNullOrWhiteSpace(Commands.SignCode))
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

        if (string.IsNullOrWhiteSpace(ConfigFiles.CodesignIdentity) || string.IsNullOrWhiteSpace(Commands.Codesign) || string.IsNullOrWhiteSpace(Commands.Productsign))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if signing with notarize is possible given the current configuration
    /// </summary>
    /// <returns></returns>
    public bool IsNotarizePossible()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        if (string.IsNullOrWhiteSpace(ConfigFiles.NotarizeProfile))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if building MSI files is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if MSI building is possible</returns>
    public bool IsMSIBuildPossible()
    {
        if (string.IsNullOrWhiteSpace(Commands.Wix))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if building MacOS packages is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if MacOS package building is possible</returns>
    public bool IsMacPkgBuildPossible()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        return true;
    }

    /// <summary>
    /// Checks if building Docker images is possible given the current configuration
    /// </summary>
    /// <returns>A boolean indicating if Docker image building is possible</returns>
    public bool IsDockerBuildPossible()
    {
        if (string.IsNullOrWhiteSpace(Commands.Docker))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if AWS uploads are possible
    /// </summary>
    /// <returns>A boolean indicating if AWS uploading is possible</returns>
    public bool IsAwsUploadPossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.AwsUploadProfile) || string.IsNullOrWhiteSpace(ConfigFiles.AwsUploadBucket))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if Github uploads are possible
    /// </summary>
    public bool IsGithubUploadPossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.GithubTokenFile))
            return false;

        if (!File.Exists(ConfigFiles.GithubTokenFile))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if update server reloads are possible
    /// </summary>
    /// <returns>A boolean indicating if update server reloads are possible</returns>
    public bool IsUpdateServerReloadPossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.ReloadUpdatesApiKey))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if Discourse announcements are possible
    /// </summary>
    /// <returns>A boolean indicating if Discourse announcements are possible</returns>
    public bool IsDiscourseAnnouncePossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.DiscourseTokenFile))
            return false;

        if (!File.Exists(ConfigFiles.DiscourseTokenFile))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if GPG signing is possible
    /// </summary>
    /// <returns>A boolean indicating if GPG signing is possible</returns>
    public bool IsGpgPossible()
    {
        if (string.IsNullOrWhiteSpace(ConfigFiles.GpgKeyfile) || string.IsNullOrWhiteSpace(Commands.Gpg))
            return false;

        if (!File.Exists(ConfigFiles.GpgKeyfile))
            return false;

        return true;
    }

    /// <summary>
    /// Determines if creating a Synology package is possible.
    /// </summary>
    /// <returns><c>true</c> if creating a Synology package is possible; otherwise, <c>false</c>.</returns>
    public bool IsSynologyPkgPossible() => false;
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
/// <param name="NotarizeProfile">The profile to use for MacOS notarization</param>
/// <param name="AwsUploadProfile">The profile used by the aws-cli tool for uploads</param>
/// <param name="AwsUploadBucket">The S3 bucket to upload files to</param>
public record ConfigFiles(
    string[] UpdaterKeyfile,
    string GpgKeyfile,
    string AuthenticodePfxFile,
    string AuthenticodePasswordFile,
    string GithubTokenFile,
    string DiscourseTokenFile,
    string CodesignIdentity,
    string NotarizeProfile,
    string AwsUploadProfile,
    string AwsUploadBucket,
    string ReloadUpdatesApiKey
)
{
    /// <summary>
    /// Parses an environment file and sets the environment variables,
    /// similar to the `source` command in bash
    /// </summary>
    /// <param name="path">The path to the file</param>
    private static void ParseEnvironmentFile(string path)
    {
        if (File.Exists(path))
        {
            var kvp = File.ReadAllLines(path)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.IndexOf('=') > 0)
                .Select(x => x.StartsWith("export ") ? x.Substring("export ".Length).Trim() : x)
                .Select(x => x.Trim().Split("=", 2))
                .Where(x => x.Length == 2)
                .Select(x => new { Key = x[0], Value = x[1] });

            foreach (var k in kvp)
                Environment.SetEnvironmentVariable(k.Key, k.Value);
        }
    }

    /// <summary>
    /// Generates a new config files instance
    /// </summary>
    /// <param name="channel">The release channel</param>
    /// <returns>The config files instance</returns>

    public static ConfigFiles Create(ReleaseChannel channel)
    {
        // Grab the shared configuration
        ParseEnvironmentFile(ExpandEnv("BUILD_SETTINGS_FILE", "${HOME}/.config/duplicati-build-settings"));

        // Override the configuration for the release type, if any
        ParseEnvironmentFile(ExpandEnv(channel switch
        {
            ReleaseChannel.Stable or
            ReleaseChannel.Beta or
            ReleaseChannel.Experimental or
            ReleaseChannel.Canary => "${HOME}/.config/signkeys/Duplicati/release-build-settings",
            ReleaseChannel.Nightly => "${HOME}/.config/signkeys/Duplicati/nightly-build-settings-nightly",
            ReleaseChannel.Debug => "${HOME}/.config/signkeys/Duplicati/debug-build-settings",
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        }));

        // Override the configuration for the release channel, if any
        ParseEnvironmentFile(ExpandEnv($"${{HOME}}/.config/signkeys/Duplicati/${channel.ToString().ToLowerInvariant()}-build-settings"));

        return new(
            ExpandEnv("UPDATER_KEYFILE", "${HOME}/.config/signkeys/Duplicati/updater-release.key").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ExpandEnv("GPG_KEYFILE", "${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"),
            ExpandEnv("AUTHENTICODE_PFXFILE", "${HOME}/.config/signkeys/Duplicati/authenticode.pfx"),
            ExpandEnv("AUTHENTICODE_PASSWORD", "${HOME}/.config/signkeys/Duplicati/authenticode.key"),
            ExpandEnv("GITHUB_TOKEN_FILE", "${HOME}/.config/github-api-token"),
            ExpandEnv("DISCOURSE_TOKEN_FILE", "${HOME}/.config/discourse-api-token"),
            ExpandEnv("CODESIGN_IDENTITY", ""),
            ExpandEnv("NOTARIZE_PROFILE", "duplicati-notarize"),
            ExpandEnv("AWS_UPLOAD_PROFILE", "duplicati-upload"),
            ExpandEnv("AWS_UPLOAD_BUCKET", "updates.duplicati.com"),
            ExpandEnv("RELOAD_UPDATES_API_KEY", "")
        );
    }
}

/// <summary>
/// Configuration of commands used by the build script
/// </summary>
/// <param name="Dotnet">The &quot;build&quot; command</param>
/// <param name="Gpg">The &quot;gpg&quot; command</param>
/// <param name="AwsCli">The &quot;aws&quot; command</param>
/// <param name="SignCode">The &quot;osslsigncode&quot; command</param>
/// <param name="Jsign">The &quot;jsign&quot; command</param>
/// <param name="Codesign">The &quot;codesign&quot; command</param>
/// <param name="Productsign">The &quot;productsign&quot; command</param>
/// <param name="Wix">The &quot;wix&quot; command</param>
/// <param name="Docker">The &quot;docker&quot; command</param>
/// <param name="Npm">The &quot;npm&quot; command</param>
public record Commands(
    string Dotnet,
    string? Gpg,
    string? SignCode,
    string? JSign,
    string? Codesign,
    string? Productsign,
    string? Wix,
    string? Docker,
    string? Npm
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
            FindCommand(OperatingSystem.IsWindows() ? "signtool.exe" : "osslsigncode", "SIGNTOOL"),
            FindCommand("jsign", "JSIGNTOOL"),
            OperatingSystem.IsMacOS() ? FindCommand("codesign", "CODESIGN") : null,
            OperatingSystem.IsMacOS() ? FindCommand("productsign", "PRODUCTSIGN") : null,
            FindCommand(OperatingSystem.IsWindows() ? "wix" : "wixl", "WIX"),
            FindCommand("docker", "DOCKER"),
            FindCommand("npm", "NPM")
        );
}

/// <summary>
/// Extra settings used by the build script, that are not expected to be changed often
/// </summary>
/// <param name="UpdateFromIncompatibleVersionUrl">The URL to use for clients upgrading from earlier versions</param>
/// <param name="GenericUpdatePageUrl">The URL to redirect to when the update has no specific package</param>
/// <param name="PackageUrls">The urls where packages are stored</param>
/// <param name="UpdaterUrls">The urls where manifest files are stored</param>
public record ExtraSettings(
    string UpdateFromIncompatibleVersionUrl,
    string GenericUpdatePageUrl,
    string[] PackageUrls,
    string[] UpdaterUrls
)
{
    /// <summary>
    /// Generates a new extra settings instance
    /// </summary>
    /// <returns>The extra settings instance</returns>
    public static ExtraSettings Create()
        => new(
            GetEnvKey("UPDATE_FROM_INCOMPATIBLE_URL", "https://duplicati.com/download-dynamic?channel=${RELEASE_CHANNEL}&update_from=${RELEASE_VERSION}"),
            GetEnvKey("GENERIC_UPDATE_PAGE_URL", "https://duplicati.com/download-dynamic?channel=${RELEASE_CHANNEL}&from=${RELEASE_VERSION}"),
            GetEnvKey("PACKAGE_URLS", "https://updates.duplicati.com/${RELEASE_CHANNEL}/${FILENAME};https://alt.updates.duplicati.com/${RELEASE_CHANNEL}/${FILENAME}").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            GetEnvKey("UPDATER_URLS", "https://updates.duplicati.com/${RELEASE_CHANNEL}/${FILENAME};https://alt.updates.duplicati.com/${RELEASE_CHANNEL}/${FILENAME}").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
}
