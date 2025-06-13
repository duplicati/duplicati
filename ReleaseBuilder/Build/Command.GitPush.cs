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
            await ProcessHelper.Execute([
                    "git", "add",
                    "ReleaseBuilder/build_version.txt",
                    "changelog.txt"
                ], workingDirectory: baseDir);

            // Make a commit
            await ProcessHelper.Execute([
                    "git", "commit",
                    "-m", $"Version bump to v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                    "-m", "You can download this build from: ",
                    "-m", $"Binaries: https://updates.duplicati.com/{releaseInfo.Channel.ToString().ToLowerInvariant()}/?version={releaseInfo.Version}",
                    "-m", $"Signature file: https://updates.duplicati.com/{releaseInfo.Channel.ToString().ToLowerInvariant()}/duplicati-{releaseInfo.ReleaseName}.signatures.zip"
                ], workingDirectory: baseDir);

            // And tag the release
            await ProcessHelper.Execute([
                    "git", "tag", $"v{releaseInfo.Version}-{releaseInfo.ReleaseName}",
                ], workingDirectory: baseDir);

            // Then push the release
            await ProcessHelper.Execute(["git", "push", "-u", "origin", "head"], workingDirectory: baseDir);
            await ProcessHelper.Execute(["git", "push", "--tags"], workingDirectory: baseDir);
        }
    }
}
