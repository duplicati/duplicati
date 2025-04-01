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

                    await ProcessHelper.ExecuteWithOutput([
                        "dotnet", "new", "sln",
                        "--name", Path.GetFileNameWithoutExtension(tmpslnfile),
                        "--output", buildDir
                    ], logStream).ConfigureAwait(false);

                    // Add the projects to the solution
                    foreach (var proj in actualBuildProjects)
                    {
                        var projpath = Path.Combine(baseDir, proj);
                        if (!File.Exists(projpath))
                            throw new FileNotFoundException($"Project file {projpath} not found");

                        await ProcessHelper.ExecuteWithOutput([
                            "dotnet", "sln", tmpslnfile,
                            "add", projpath
                        ], logStream).ConfigureAwait(false);
                    }
                }

                foreach (var s in tk)
                {
                    temporarySolutionFiles[s] = tmpslnfile;
                    targetExecutables[s] = actualBuildProjects;
                }
            }

            // Start the task now
            var verifyRootJson = Verify.AnalyzeProject(Path.Combine(baseDir, "Duplicati.sln"));

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
                    Console.WriteLine($"Building {target.BuildTargetString} ...");

                    // Fix any RIDs that differ from .NET SDK
                    var archstring = target.Arch switch
                    {
                        ArchType.Arm7 => $"{target.OSString}-arm",
                        _ => target.BuildArchString
                    };

                    // TODO: Self contained builds are bloating the build size
                    // Alternative is to require the .NET runtime to be installed

                    var command = new string[] {
                            "dotnet", "publish", temporarySolutionFiles[target],
                            "--configuration", "Release",
                            "--output", tmpfolder,
                            "-r", archstring,
                            $"/p:AssemblyVersion={releaseInfo.Version}",
                            $"/p:Version={releaseInfo.Version}-{releaseInfo.Channel}-{releaseInfo.Timestamp:yyyyMMdd}",
                            "--self-contained", useHostedBuilds ? "false" : "true"
                        };

                    try
                    {
                        await ProcessHelper.ExecuteWithLog(command, workingDirectory: tmpfolder, logFolder: logFolder, logFilename: (pid, isStdOut) => $"{target.BuildTargetString}.{pid}.{(isStdOut ? "stdout" : "stderr")}.log");
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
                    await Verify.VerifyDuplicatedVersionsAreMaxVersions(tmpfolder, await verifyRootJson.ConfigureAwait(false));

                    // Move the final build to the output folder
                    Directory.Move(tmpfolder, outputFolder);
                }

                Console.WriteLine("Completed!");
            }
        }
    }
}