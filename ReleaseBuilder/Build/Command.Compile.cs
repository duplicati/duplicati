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
    /// Main compilation of projects
    /// </summary>
    private static class Compile
    {
        /// <summary>
        /// Folders to remove from the build folder
        /// </summary>
        private static readonly IReadOnlySet<string> TempBuildFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj"
        };

        /// <summary>
        /// Removes all temporary build folders from the base folder
        /// </summary>
        /// <param name="basefolder">The folder to clean</param>
        /// <returns>>A task that completes when the clean is done</returns>
        private static Task RemoveAllBuildTempFolders(string basefolder)
        {
            // Remove all obj and bin folders
            foreach (var folder in Directory.GetDirectories(basefolder, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(folder);
                if (TempBuildFolders.Contains(name) && Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Builds the projects listed in <paramref name="sourceProjects"/> for the distinct <paramref name="buildTargets"/>
        /// </summary>
        /// <param name="baseDir">The base solution folder</param>
        /// <param name="buildDir">The folder where builds should be placed</param>
        /// <param name="sourceProjects">The projects to build</param>
        /// <param name="windowsOnlyProjects">Projects that are only for the Windows targets</param>
        /// <param name="buildTargets">The targets to build</param>
        /// <param name="releaseInfo">The release info to use for the build</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>A task that completes when the build is done</returns>
        public static async Task BuildProjects(string baseDir, string buildDir, Dictionary<InterfaceType, IEnumerable<string>> sourceProjects, IEnumerable<string> windowsOnlyProjects, IEnumerable<PackageTarget> buildTargets, ReleaseInfo releaseInfo, bool keepBuilds, RuntimeConfig rtcfg, bool useHostedBuilds)
        {
            // For tracing, create a log folder and store all logs there
            var logFolder = Path.Combine(buildDir, "logs");
            Directory.CreateDirectory(logFolder);

            // Get the unique build targets (ignoring the package type)
            var buildArchTargets = buildTargets.DistinctBy(x => (x.OS, x.Arch, x.Interface)).ToArray();

            if (buildArchTargets.Length == 1)
                Console.WriteLine($"Building single release: {buildArchTargets.First().BuildTargetString}");
            else
                Console.WriteLine($"Building {buildArchTargets.Length} versions");

            var buildOutputFolders = buildArchTargets.ToDictionary(x => x, x => Path.Combine(buildDir, x.BuildTargetString));
            var temporarySolutionFiles = new Dictionary<PackageTarget, string>();
            var targetExecutables = new Dictionary<PackageTarget, List<string>>();
            var distinctSolutions = buildArchTargets.GroupBy(x => $"{x.InterfaceString}{(x.OS == OSType.Windows ? $"-{x.OSString}" : "")}").ToArray();
            Console.WriteLine($"Creating {distinctSolutions.Length} temporary solution files");
            foreach (var tk in distinctSolutions)
            {
                // Don't create a solution if all the output folders exist
                if (tk.All(x => Directory.Exists(buildOutputFolders[x])))
                    continue;

                var tmpslnfile = Path.Combine(buildDir, $"Duplicati-{tk.Key}.sln");

                var target = tk.First();
                if (!sourceProjects.TryGetValue(target.Interface, out var buildProjects))
                    throw new InvalidOperationException($"No projects found for {tk.Key}");

                var actualBuildProjects = buildProjects
                    .Where(x => target.OS == OSType.Windows || !windowsOnlyProjects.Contains(x))
                    .ToList();

                // Faster debugging, keep the solution file
                if (!File.Exists(tmpslnfile))
                {
                    var logOut = Path.Combine(logFolder, $"create-{Path.GetFileName(tmpslnfile)}.log");
                    using var logStream = new FileStream(logOut, FileMode.Create, FileAccess.Write, FileShare.Read);

                    var tmpslnfile2 = Path.Combine(buildDir, $"tmp-Duplicati-{tk.Key}.sln");
                    if (File.Exists(tmpslnfile2))
                        File.Delete(tmpslnfile2);

                    await ProcessHelper.ExecuteWithOutput([
                        "dotnet", "new", "sln",
                        "--name", Path.GetFileNameWithoutExtension(tmpslnfile2),
                        "--output", buildDir
                    ], logStream).ConfigureAwait(false);

                    // Add the projects to the solution
                    foreach (var proj in actualBuildProjects)
                    {
                        var projpath = Path.Combine(baseDir, proj);
                        if (!File.Exists(projpath))
                            throw new FileNotFoundException($"Project file {projpath} not found");

                        await ProcessHelper.ExecuteWithOutput([
                            "dotnet", "sln", tmpslnfile2,
                            "add", projpath
                        ], logStream).ConfigureAwait(false);
                    }

                    File.Move(tmpslnfile2, tmpslnfile, true);
                }

                foreach (var s in tk)
                {
                    temporarySolutionFiles[s] = tmpslnfile;
                    targetExecutables[s] = actualBuildProjects;
                }
            }

            var verifyRootJson = new Verify.RootJson(1, "", []);

            // Set up analysis for the projects, if we are building any
            if (!keepBuilds || buildOutputFolders.Any(x => !Directory.Exists(x.Value)))
            {
                // Make sure there is no cache from previous builds
                await RemoveAllBuildTempFolders(baseDir).ConfigureAwait(false);
                await Verify.AnalyzeProject(Path.Combine(baseDir, "Duplicati.sln")).ConfigureAwait(false);
            }

            foreach ((var target, var outputFolder) in buildOutputFolders)
            {
                // Faster iteration for debugging is to keep the build folder
                if (keepBuilds && Directory.Exists(outputFolder))
                {
                    Console.WriteLine($"Skipping build as output exists for {target.BuildTargetString}");
                }
                else
                {
                    var tmpfolder = Path.Combine(buildDir, target.BuildTargetString + "-tmp");
                    if (Directory.Exists(tmpfolder))
                        Directory.Delete(tmpfolder, true);

                    Console.WriteLine($"Building {target.BuildTargetString} ...");

                    // Fix any RIDs that differ from .NET SDK
                    var archstring = target.Arch switch
                    {
                        ArchType.Arm7 => $"{target.OSString}-arm",
                        _ => target.BuildArchString
                    };

                    var buildTime = $"{DateTime.Now:yyyyMMdd-HHmmss}";
                    string logNameFn(int pid, bool isStdOut)
                        => $"{target.BuildTargetString}.{buildTime}.{(isStdOut ? "stdout" : "stderr")}.log";

                    // Make sure there is no cache from previous builds
                    await RemoveAllBuildTempFolders(baseDir).ConfigureAwait(false);

                    // TODO: Self contained builds are bloating the build size
                    // Alternative is to require the .NET runtime to be installed

                    var command = new string[] {
                        "dotnet", "publish", temporarySolutionFiles[target],
                        "--configuration", "Release",
                        "--output", tmpfolder,
                        "--runtime", archstring,
                        "--self-contained", useHostedBuilds ? "false" : "true",
                        // $"-p:UseSharedCompilation=false",
                        $"-p:AssemblyVersion={releaseInfo.Version}",
                        $"-p:Version={releaseInfo.Version}-{releaseInfo.Channel}-{releaseInfo.Timestamp:yyyyMMdd}",
                    };

                    try
                    {
                        await ProcessHelper.ExecuteWithLog(
                            command,
                            workingDirectory: tmpfolder,
                            logFolder: logFolder,
                            logFilename: logNameFn).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error building for {target.BuildTargetString}: {ex.Message}");
                        throw;
                    }

                    // Perform any post-build steps, cleaning and signing as needed
                    await PostCompile.PrepareTargetDirectory(baseDir, tmpfolder, target, rtcfg, keepBuilds);
                    await Verify.VerifyTargetDirectory(tmpfolder, target);
                    await Verify.VerifyExecutables(tmpfolder, targetExecutables[target], target);
                    await Verify.VerifyDuplicatedVersionsAreMaxVersions(tmpfolder, verifyRootJson);

                    // Move the final build to the output folder
                    Directory.Move(tmpfolder, outputFolder);
                }

                Console.WriteLine("Completed!");
            }
        }
    }
}