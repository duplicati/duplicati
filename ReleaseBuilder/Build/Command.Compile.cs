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
        /// <param name="guiProjects">Projects that are only needed for GUI builds</param>
        /// <param name="buildTargets">The targets to build</param>
        /// <param name="releaseInfo">The release info to use for the build</param>
        /// <param name="keepBuilds">A flag that allows re-using existing builds</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>A task that completes when the build is done</returns>
        public static async Task BuildProjects(string baseDir, string buildDir, IEnumerable<string> sourceProjects, IEnumerable<string> windowsOnlyProjects, IEnumerable<string> guiProjects, IEnumerable<PackageTarget> buildTargets, ReleaseInfo releaseInfo, bool keepBuilds, RuntimeConfig rtcfg)
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

            foreach (var target in buildArchTargets)
            {
                var outputFolder = Path.Combine(buildDir, target.BuildTargetString);

                // Faster iteration for debugging is to keep the build folder
                if (keepBuilds && Directory.Exists(outputFolder))
                {
                    Console.WriteLine($"Skipping build as output exists for {target.BuildTargetString}");
                }
                else
                {
                    var tmpfolder = Path.Combine(buildDir, target.BuildTargetString + "-tmp");
                    Console.WriteLine($"Building {target.BuildTargetString} ...");

                    foreach (var proj in sourceProjects)
                    {
                        if (target.OS != OSType.Windows && windowsOnlyProjects.Contains(proj))
                            continue;

                        if (target.Interface == InterfaceType.Cli && guiProjects.Contains(proj))
                            continue;

                        // TODO: Self contained builds are bloating the build size
                        // Alternative is to require the .NET runtime to be installed

                        // Fix any RIDs that differ from .NET SDK
                        var archstring = target.Arch switch
                        {
                            ArchType.Arm7 => $"{target.OSString}-arm",
                            _ => target.BuildArchString
                        };

                        var command = new string[] {
                            "dotnet", "publish", proj,
                            "-c", "Release",
                            "-o", tmpfolder,
                            "-r", archstring,
                            $"/p:AssemblyVersion={releaseInfo.Version}",
                            $"/p:Version={releaseInfo.Version}-{releaseInfo.Channel}-{releaseInfo.Timestamp:yyyyMMdd}",
                            "--self-contained", "true"
                        };
                        await ProcessHelper.ExecuteWithLog(command, workingDirectory: tmpfolder, logFolder: logFolder, logFilename: (pid, isStdOut) => $"{Path.GetFileNameWithoutExtension(proj)}.{target.BuildTargetString}.{pid}.{(isStdOut ? "stdout" : "stderr")}.log");
                    }

                    // Perform any post-build steps, cleaning and signing as needed
                    await PostCompile.PrepareTargetDirectory(baseDir, tmpfolder, target.OS, target.Arch, target.BuildTargetString, rtcfg, keepBuilds);

                    Directory.Move(tmpfolder, outputFolder);
                }

                Console.WriteLine("Completed!");
            }
        }
    }
}