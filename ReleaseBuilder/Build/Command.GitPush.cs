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
            // Add modified files
            await ProcessHelper.Execute(new[] {
                    "git", "add",
                    "ReleaseBuilder/build_version.txt",
                    "changelog.txt"
                }, workingDirectory: baseDir);

            // Make a commit

            // TODO: Since there is no longer a single binary, use github releases?
            await ProcessHelper.Execute(new[] {
                    "git", "commit",
                    "-m", $"Version bump to v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                    "-m", "You can download this build from: ",
                    "-m", $"Binaries: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip",
                    "-m", $"Signature file: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip.sig",
                    "-m", $"ASCII signature file: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip.sig.asc",
                    "-m", $"MD5: {releaseInfo.ReleaseName}.zip.md5",
                    "-m", $"SHA1: {releaseInfo.ReleaseName}.zip.sha1",
                    "-m", $"SHA256: {releaseInfo.ReleaseName}.zip.sha256"
                }, workingDirectory: baseDir);

            // And tag the release
            await ProcessHelper.Execute(new[] {
                    "git", "tag", $"v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                    "-m", "You can download this build from: ",
                    "-m", $"Binaries: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip",
                    "-m", $"Signature file: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip.sig",
                    "-m", $"ASCII signature file: https://updates.duplicati.com/{releaseInfo.Channel}/{releaseInfo.ReleaseName}.zip.sig.asc",
                    "-m", $"MD5: {releaseInfo.ReleaseName}.zip.md5",
                    "-m", $"SHA1: {releaseInfo.ReleaseName}.zip.sha1",
                    "-m", $"SHA256: {releaseInfo.ReleaseName}.zip.sha256"
                }, workingDirectory: baseDir);

            // The push the release
            await ProcessHelper.Execute(new[] { "git", "push", "--tags" }, workingDirectory: baseDir);
        }
    }
}
