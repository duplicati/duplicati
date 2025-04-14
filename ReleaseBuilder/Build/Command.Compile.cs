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
using System.Reflection;
using System.Text.RegularExpressions;

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
        /// <param name="useHostedBuilds">A flag that allows using hosted builds</param>
        /// <param name="disableCleanSource">A flag that allows skipping the clean source step</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>A task that completes when the build is done</returns>
        public static async Task BuildProjects(string baseDir, string buildDir, Dictionary<InterfaceType, IEnumerable<string>> sourceProjects, IEnumerable<string> windowsOnlyProjects, IEnumerable<PackageTarget> buildTargets, ReleaseInfo releaseInfo, bool keepBuilds, RuntimeConfig rtcfg, bool useHostedBuilds, bool disableCleanSource, bool allowAssemblyMismatch)
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
            var verifyRootJson = new Verify.RootJson(1, "", []);

            // Set up analysis for the projects, if we are building any
            if (!keepBuilds || buildOutputFolders.Any(x => !Directory.Exists(x.Value)))
            {
                // Make sure there is no cache from previous builds
                if (!disableCleanSource)
                    await RemoveAllBuildTempFolders(baseDir).ConfigureAwait(false);
                verifyRootJson = await Verify.AnalyzeProject(Path.Combine(baseDir, "Duplicati.sln")).ConfigureAwait(false);
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
                    Directory.CreateDirectory(tmpfolder);

                    Console.WriteLine($"Building {target.BuildTargetString} ...");

                    // Fix any RIDs that differ from .NET SDK
                    var archstring = target.Arch switch
                    {
                        ArchType.Arm7 => $"{target.OSString}-arm",
                        _ => target.BuildArchString
                    };

                    var buildTime = $"{DateTime.Now:yyyyMMdd-HHmmss}";

                    // Make sure there is no cache from previous builds
                    if (!disableCleanSource)
                        await RemoveAllBuildTempFolders(baseDir).ConfigureAwait(false);

                    // TODO: Self contained builds are bloating the build size
                    // Alternative is to require the .NET runtime to be installed

                    if (!sourceProjects.TryGetValue(target.Interface, out var buildProjects))
                        throw new InvalidOperationException($"No projects found for {target.Interface}");

                    var actualBuildProjects = buildProjects
                        .Where(x => target.OS == OSType.Windows || !windowsOnlyProjects.Contains(x))
                        .ToList();

                    var incorrectAssemblyVersions = new Dictionary<string, HashSet<Version>>();

                    foreach (var project in actualBuildProjects)
                    {
                        string logNameFn(int pid, bool isStdOut)
                            => $"{Path.GetFileNameWithoutExtension(project)}-{target.BuildTargetString}.{buildTime}.{(isStdOut ? "stdout" : "stderr")}.log";

                        var stdoutLog = logNameFn(0, true);
                        var stderrLog = logNameFn(0, false);

                        var buildfolder = Path.Combine(buildDir, target.BuildTargetString + "-build");
                        if (Directory.Exists(buildfolder))
                            Directory.Delete(buildfolder, true);

                        var command = new string[] {
                            "dotnet", "publish", project,
                            "--configuration", "Release",
                            "--output", buildfolder,
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

                        // Check that there are no debug builds in the log
                        var logData = File.ReadAllText(Path.Combine(logFolder, stdoutLog));
                        if (Regex.Match(logData, @"[\\/]bin[\\/]Debug[\\/]", RegexOptions.IgnoreCase).Success)
                            throw new InvalidOperationException($"Build for {target.BuildTargetString} failed. Debug build found in log: {stdoutLog}.");

                        EnvHelper.CopyDirectory(buildfolder, tmpfolder, true, (current, update) =>
                        {
                            var c = CompareAssemblyVersions(current.FullName, update.FullName);
                            if (c != 0)
                            {
                                if (!incorrectAssemblyVersions.TryGetValue(Path.GetFileName(current.FullName), out var versions))
                                    incorrectAssemblyVersions[Path.GetFileName(current.FullName)] = versions = new HashSet<Version>();

                                versions.Add(AssemblyName.GetAssemblyName(current.FullName).Version ?? new Version(0, 0));
                                versions.Add(AssemblyName.GetAssemblyName(update.FullName).Version ?? new Version(0, 0));
                            }
                            return c >= 0;
                        });
                        // EnvHelper.CopyDirectory(buildfolder, tmpfolder, true);
                        Directory.Delete(buildfolder, true);
                    }

                    // Perform any post-build steps, cleaning and signing as needed
                    await PostCompile.PrepareTargetDirectory(baseDir, tmpfolder, target, rtcfg, keepBuilds);
                    await Verify.VerifyTargetDirectory(tmpfolder, target);
                    await Verify.VerifyExecutables(tmpfolder, actualBuildProjects, target);
                    await Verify.VerifyDuplicatedVersionsAreMaxVersions(tmpfolder, verifyRootJson);

                    if (incorrectAssemblyVersions.Count > 0)
                    {
                        Console.WriteLine($"Incorrect assembly versions found in {target.BuildTargetString}:");
                        foreach (var file in incorrectAssemblyVersions.Keys.Order())
                            Console.WriteLine($"  {file}: {string.Join(", ", incorrectAssemblyVersions[file].OrderByDescending(x => x))}");

                        throw new InvalidOperationException($"Incorrect assembly versions found in {target.BuildTargetString}");
                    }

                    // Move the final build to the output folder
                    Directory.Move(tmpfolder, outputFolder);
                }

                Console.WriteLine("Completed!");
            }
        }

        /// <summary>
        /// Checks if the file is a PE file
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <returns>>True if the file is a PE file, false otherwise</returns>
        private static bool IsPEFile(string path)
        {
            if (!File.Exists(path)) return false;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            // Check for MZ header
            if (br.ReadUInt16() != 0x5A4D) // "MZ"
                return false;

            fs.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = br.ReadInt32();

            if (peHeaderOffset <= 0 || peHeaderOffset > fs.Length - 4)
                return false;

            fs.Seek(peHeaderOffset, SeekOrigin.Begin);
            var peSignature = br.ReadUInt32();

            return peSignature == 0x00004550; // "PE\0\0"
        }

        /// <summary>
        /// Compares the assembly versions of two files
        /// </summary>
        /// <param name="p1">Path to the first file</param>
        /// <param name="p2">Path to the second file</param>
        /// <returns>0 if the versions are equal, -1 if the first is is largest, 1 if the second is largest</returns>
        private static int CompareAssemblyVersions(string p1, string p2)
        {
            try
            {
                if (!IsPEFile(p1))
                    return 0;

                var version1 = AssemblyName.GetAssemblyName(p1)?.Version;
                var version2 = AssemblyName.GetAssemblyName(p2)?.Version;

                if (version1 == null || version2 == null)
                    return 0;
                if (version1 > version2)
                    return -1;
                if (version1 < version2)
                    return 1;
            }
            catch
            {
            }

            return 0;

        }
    }
}