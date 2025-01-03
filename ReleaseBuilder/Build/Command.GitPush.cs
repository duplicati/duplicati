namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Implementation of the git push command
    /// </summary>
    private static class GitPush
    {
        /// <summary>
        /// Tags the release and pushes it to the repository
        /// </summary>
        /// <param name="baseDir">The base git dir</param>
        /// <param name="releaseInfo">The release info</param>
        /// <returns>A task that completes when the push is done</returns>
        public static async Task TagAndPush(string baseDir, ReleaseInfo releaseInfo)
        {
            // Write the published version to a file
            File.WriteAllText(Path.Combine(baseDir, "ReleaseBuilder", "build_version.txt"), releaseInfo.Version.ToString());

            // Add modified files
            await ProcessHelper.Execute(new[] {
                    "git", "add",
                    "ReleaseBuilder/build_version.txt",
                    "changelog.txt"
                }, workingDirectory: baseDir);

            // Make a commit
            await ProcessHelper.Execute(new[] {
                    "git", "commit",
                    "-m", $"Version bump to v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                    "-m", "You can download this build from: ",
                    "-m", $"Binaries: https://updates.duplicati.com/{releaseInfo.Channel.ToString().ToLowerInvariant()}/?version={releaseInfo.Version}",
                    "-m", $"Signature file: https://updates.duplicati.com/{releaseInfo.Channel.ToString().ToLowerInvariant()}/duplicati-{releaseInfo.ReleaseName}.signatures.zip"
                }, workingDirectory: baseDir);

            // And tag the release
            await ProcessHelper.Execute(new[] {
                    "git", "tag", $"v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                }, workingDirectory: baseDir);

            // Then push the release
            await ProcessHelper.Execute(new[] { "git", "push", "-u", "origin", "head" }, workingDirectory: baseDir);
            await ProcessHelper.Execute(new[] { "git", "push", "--tags" }, workingDirectory: baseDir);
        }
    }
}
