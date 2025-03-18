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
using System.Net.Http.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Implementation of the various upload functions
    /// </summary>
    static class Upload
    {
        /// <summary>
        /// The Github structure for post data
        /// </summary>
        /// <param name="tag_name">The tag to create the release for</param>
        /// <param name="target_commitish">The commit hash or name</param>
        /// <param name="name">The name of the release</param>
        /// <param name="body">The release message</param>
        /// <param name="draft">Flag toggling draft release</param>
        /// <param name="prerelease">Flag toggling the prerelease tag</param>
        /// <param name="generate_release_notes">Flag togling release note generator</param>
        private record GhReleaseInfo(
            string tag_name,
            string target_commitish,
            string name,
            string body,
            bool draft,
            bool prerelease,
            bool generate_release_notes
        );

        /// <summary>
        ///  The Github structure for response data
        /// </summary>
        /// <param name="assets_url">The URL to the assets</param>
        /// <param name="url">The URL to the release</param>
        /// <param name="id">The release ID</param>
        private record GhReleaseResponse(
            string assets_url,
            string url,
            int id
        );

        /// <summary>
        /// Uploads a local file to a remote location
        /// </summary>
        /// <param name="Path">The path to the file on the local filesystem</param>
        /// <param name="Name">The name of the file on the remote target</param>
        public record UploadFile(string Path, string Name);

        /// <summary>
        /// Single entry in the installer json support file
        /// </summary>
        /// <param name="url">The remote package url</param>
        /// <param name="filename">The filename of the package</param>
        /// <param name="md5">The MD5 hash of the file</param>
        /// <param name="sha256">The SHA256 hash of the file</param>
        /// <param name="size">The size of the file</param>
        private record PackageEntry(string url, string filename, string md5, string sha256, long size);

        /// <summary>
        /// Creates the package list in JSON format
        /// </summary>
        /// <param name="packages">The packages to include in the file</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>The JSON string</returns>
        public static string CreatePackageJson(IEnumerable<CreatePackage.BuiltPackage> packages, RuntimeConfig rtcfg)
        {
            var entries = packages.Select(f => (f.Target.PackageTargetString, Entry: new PackageEntry(
                url: $"https://updates.duplicati.com/{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/{System.Web.HttpUtility.UrlEncode(Path.GetFileName(f.CreatedFile))}",
                filename: Path.GetFileName(f.CreatedFile),
                md5: CalculateHash(f.CreatedFile, "md5"),
                sha256: CalculateHash(f.CreatedFile, "sha256"),
                size: new FileInfo(f.CreatedFile).Length
            )))
            .ToDictionary(x => x.PackageTargetString, x => x.Entry);

            return System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Upload release files to S3 storage
        /// </summary>
        /// <param name="files">The files to upload</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <param name="propagateTo">The release channels to propagate to</param>
        /// <returns>An awaitable task</returns>
        public static async Task UploadToS3(IEnumerable<UploadFile> files, RuntimeConfig rtcfg, IEnumerable<ReleaseChannel> propagateTo)
        {
            var totalSize = files.Sum(f => new FileInfo(f.Path).Length);
            Console.WriteLine($"Uploading {files.Count()} files ({Duplicati.Library.Utility.Utility.FormatSizeString(totalSize)}) to S3...");

            var chain = new Amazon.Runtime.CredentialManagement.CredentialProfileStoreChain();
            if (!chain.TryGetAWSCredentials(rtcfg.Configuration.ConfigFiles.AwsUploadProfile, out var awsCredentials))
                throw new Exception($"The aws-cli profile '{rtcfg.Configuration.ConfigFiles.AwsUploadProfile}' could not be found.");

            chain.TryGetProfile(rtcfg.Configuration.ConfigFiles.AwsUploadProfile, out var awsProfile);
            var config = new AmazonS3Config
            {
                RegionEndpoint = awsProfile?.Region ?? RegionEndpoint.USEast1,
                UseArnRegion = false,
                ServiceURL = awsProfile?.EndpointUrl ?? "https://s3.amazonaws.com"
            };

            using var client = new AmazonS3Client(awsCredentials, config);
            var fileTransferUtility = new TransferUtility(client);
            var size = 0L;

            foreach (var file in files)
            {
                await Duplicati.Library.Utility.RetryHelper.Retry(() =>
                    fileTransferUtility.UploadAsync(
                        file.Path,
                        rtcfg.Configuration.ConfigFiles.AwsUploadBucket,
                        $"{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/{file.Name}"
                    ), 3, TimeSpan.FromSeconds(1), CancellationToken.None);

                var filesize = new FileInfo(file.Path).Length;
                size += filesize;
                Console.WriteLine($"{size / (double)totalSize * 100:F1}% - Uploaded {file.Name} ({Duplicati.Library.Utility.Utility.FormatSizeString(filesize)})");
            }

            foreach (var channel in propagateTo)
            {
                if (channel == rtcfg.ReleaseInfo.Channel)
                    continue;

                var target = $"{channel.ToString().ToLowerInvariant()}/latest-v2.manifest";
                var source = $"{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/latest-v2.manifest";
                await Duplicati.Library.Utility.RetryHelper.Retry(() =>
                    client.CopyObjectAsync(
                        rtcfg.Configuration.ConfigFiles.AwsUploadBucket,
                        source,
                        rtcfg.Configuration.ConfigFiles.AwsUploadBucket,
                        target
                    ), 3, TimeSpan.FromSeconds(1), CancellationToken.None);
            }
        }

        /// <summary>
        /// Upload release files to Github
        /// </summary>
        /// <param name="files">The files to upload</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static async Task UploadToGithub(IEnumerable<UploadFile> files, RuntimeConfig rtcfg)
        {
            var commithash = (await ProcessHelper.ExecuteWithOutput(["git", "rev-parse", "HEAD"]))?.Trim()
                ?? throw new Exception("Failed to get the current commit hash");

            using var httpClient = new HttpClient();
            var totalSize = files.Sum(f => new FileInfo(f.Path).Length);
            Console.WriteLine($"Uploading {files.Count()} files ({Duplicati.Library.Utility.Utility.FormatSizeString(totalSize)}) to Github...");

            var ghtoken = File.ReadAllText(rtcfg.Configuration.ConfigFiles.GithubTokenFile).Trim();
            var owner = "duplicati";
            var repo = "duplicati";

            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{owner}/{repo}/releases");
            request.Headers.Add("Accept", "application/vnd.github+json");
            request.Headers.Add("Authorization", $"Bearer {ghtoken}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Add("User-Agent", "Duplicati Release Builder v1");
            request.Content = JsonContent.Create(new GhReleaseInfo(
                tag_name: $"v{rtcfg.ReleaseInfo.ReleaseName}",
                target_commitish: commithash,
                name: $"v{rtcfg.ReleaseInfo.ReleaseName}",
                body: rtcfg.ChangelogNews,
                draft: false,
                prerelease: rtcfg.ReleaseInfo.Channel != ReleaseChannel.Stable,
                generate_release_notes: false
            ));

            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var releasedata = await response.Content.ReadFromJsonAsync<GhReleaseResponse>()
                ?? throw new Exception("Failed to read release data");

            var size = 0L;

            // Upload the files to the same tag
            foreach (var file in files)
            {
                await Duplicati.Library.Utility.RetryHelper.Retry(async () =>
                {
                    request = new HttpRequestMessage(HttpMethod.Post, $"https://uploads.github.com/repos/{owner}/{repo}/releases/{releasedata.id}/assets?name={System.Net.WebUtility.UrlEncode(Path.GetFileName(file.Name))}");
                    request.Headers.Add("Accept", "application/vnd.github+json");
                    request.Headers.Add("Authorization", $"Bearer {ghtoken}");
                    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                    request.Headers.Add("User-Agent", "Duplicati Release Builder v1");

                    using var fileStream = File.OpenRead(file.Path);
                    request.Content = new StreamContent(fileStream);
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                }, 3, TimeSpan.FromSeconds(1), CancellationToken.None);

                var filesize = new FileInfo(file.Path).Length;
                size += filesize;
                Console.WriteLine($"{size / (double)totalSize * 100:F1}% - Uploaded {file.Name} ({Duplicati.Library.Utility.Utility.FormatSizeString(filesize)})");
            }
        }

        /// <summary>
        /// Reload the update server cache
        /// </summary>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static async Task ReloadUpdateServer(RuntimeConfig rtcfg, IEnumerable<ReleaseChannel> propagateTo)
        {
            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://updates.duplicati.com/reload");

            var reloadToken = rtcfg.Configuration.ConfigFiles.ReloadUpdatesApiKey;
            req.Headers.Add("X-API-KEY", reloadToken);
            req.Content = JsonContent.Create(new[] {
                $"{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/latest-v2.json",
                $"{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/latest-v2.js",
                $"{rtcfg.ReleaseInfo.Channel.ToString().ToLowerInvariant()}/latest-v2.manifest",
             }.Concat(propagateTo.Select(c => $"{c.ToString().ToLowerInvariant()}/latest-v2.manifest")));

            var response = await client.SendAsync(req);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Post the release to the Duplicati forum
        /// </summary>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static async Task PostToForum(RuntimeConfig rtcfg)
        {
            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://forum.duplicati.com/posts");

            var discourseToken = File.ReadAllText(rtcfg.Configuration.ConfigFiles.DiscourseTokenFile).Trim().Split(":", 2);
            req.Headers.Add("Api-Username", discourseToken[0]);
            req.Headers.Add("Api-Key", discourseToken[1]);
            req.Headers.Add("Accept", "application/json");
            req.Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("category", "10"),
                new KeyValuePair<string, string>("title", $"Release: {rtcfg.ReleaseInfo.Version} ({rtcfg.ReleaseInfo.Channel}) {rtcfg.ReleaseInfo.Timestamp:yyyy-MM-dd}"),
                new KeyValuePair<string, string>("raw", $"# [{rtcfg.ReleaseInfo.ReleaseName}](https://github.com/duplicati/duplicati/releases/tag/v{rtcfg.ReleaseInfo.ReleaseName})\n\n{rtcfg.ChangelogNews}")
            ]);

            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }
    }
}
