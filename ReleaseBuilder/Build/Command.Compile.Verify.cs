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
using System.Text;
using System.Text.Json;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Main compilation of projects
    /// </summary>
    private static class Verify
    {
        /// <summary>
        /// Verify that some files that are expected to be present in the target directory are there
        /// </summary>
        /// <param name="buildDir">The build directory to verify</param>
        /// <param name="target">The target to verify for</param>
        /// <returns>An awaitable task</returns>
        public static Task VerifyTargetDirectory(string buildDir, PackageTarget target)
        {
            var rootFiles = Directory.EnumerateFiles(buildDir, "*", SearchOption.TopDirectoryOnly)
                .Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(x => Path.GetFileName(x))
                .ToHashSet(Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);

            string[] extras = target.OS switch
            {
                OSType.Windows => ["Vanara.PInvoke.Kernel32.dll", "Vanara.PInvoke.VssApi.dll", "Duplicati.Library.WindowsModules.dll"],
                OSType.MacOS => [],
                OSType.Linux => [],
                _ => throw new Exception($"Not supported OS: {target.OS}")
            };

            // Random sample of files we expect
            string[] probeFiles = [
                "System.CommandLine.dll",
                "System.CommandLine.NamingConventionBinder.dll",
                "AWSSDK.S3.dll",
                "CoCoL.dll",
                "Duplicati.Library.Interface.dll",
                "Google.Apis.Auth.dll",
                "Google.Apis.Core.dll",
                "SQLiteHelper.dll",
                "Microsoft.Data.Sqlite.dll",
                "Microsoft.IdentityModel.Abstractions.dll",
                "System.Reactive.dll",
                .. extras
            ];

            var missing = probeFiles.Where(x => !rootFiles.Contains(x)).ToArray();
            if (missing.Length > 0)
                throw new Exception($"Expected files {string.Join(", ", missing)} for {target.BuildTargetString}, but were not found in build directory {buildDir}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that all expected executables are in the output
        /// </summary>
        /// <param name="buildDir">The build directory to verify</param>
        /// <param name="sourceProjects">The project files to verify</param>
        /// <param name="target">The target to verify for</param>
        /// <returns>An awaitable task</returns>
        public static Task VerifyExecutables(string buildDir, IEnumerable<string> projectFiles, PackageTarget target)
        {
            var expected = projectFiles.Select(x => Path.GetFileNameWithoutExtension(x))
                .Select(x => target.OS == OSType.Windows ? $"{x}.exe" : x);

            var missing = expected.Where(x => !File.Exists(Path.Combine(buildDir, x))).ToArray();
            if (missing.Length > 0)
                throw new Exception($"Expected files {string.Join(", ", missing)} for {target.BuildTargetString}, but were not found in build directory {buildDir}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Root entry from the dotnet list output
        /// </summary>
        /// <param name="Version">The version of the output format</param>
        /// <param name="Parameters">The parameters used to generate the output</param>
        /// <param name="Projects">The list of projects found</param>
        public sealed record RootJson(
            int Version,
            string Parameters,
            IEnumerable<ProjectJson> Projects
        );

        /// <summary>
        /// A single project
        /// </summary>
        /// <param name="Path">Full path to the csproj file</param>
        /// <param name="Frameworks">The frameworks found</param>
        public sealed record ProjectJson(
            string Path,
            IEnumerable<FrameworkJson> Frameworks
        );

        /// <summary>
        /// Contents of a framework
        /// </summary>
        /// <param name="Framework">The framework name</param>
        /// <param name="TopLevelPackages">Directly included packages</param>
        /// <param name="TransitivePackages">Packages included due to the top-level packages</param>
        public sealed record FrameworkJson(
            string Framework,
            IEnumerable<TopLevelJson> TopLevelPackages,
            IEnumerable<PackageJson> TransitivePackages
        );

        /// <summary>
        /// A top-level package
        /// </summary>
        /// <param name="Id">The package id</param>
        /// <param name="RequestedVersion">The version requested</param>
        /// <param name="ResolvedVersion">The resolved version</param>
        public sealed record TopLevelJson(
            string Id,
            string RequestedVersion,
            string ResolvedVersion
        );

        /// <summary>
        /// A transitive package
        /// </summary>
        /// <param name="Id">The package id</param>
        /// <param name="ResolvedVersion">The resolved version</param>
        public sealed record PackageJson(
            string Id,
            string ResolvedVersion
        );

        /// <summary>
        /// Executes the dotnet list command and parses the output
        /// </summary>
        /// <param name="slnpath">The path to the sln file to analyze</param>
        /// <returns>The parsed output</returns>
        public static async Task<RootJson> AnalyzeProject(string slnpath)
        {
            await ProcessHelper.ExecuteWithOutput([
                "dotnet", "restore", slnpath
            ]).ConfigureAwait(false);

            var output = await ProcessHelper.ExecuteWithOutput([
                "dotnet", "list",
                slnpath, "package",
                "--include-transitive",
                "--format", "json"
            ]).ConfigureAwait(false);

            var root = JsonSerializer.Deserialize<RootJson>(output, new JsonSerializerOptions(JsonSerializerOptions.Default) { PropertyNameCaseInsensitive = true })
                ?? throw new Exception("Failed to parse JSON output from dotnet list");
            if (root.Version != 1)
                throw new Exception($"Unexpected version {root.Version} from dotnet list");

            return root;
        }

        /// <summary>
        /// Parses a version string into a Version object
        /// </summary>
        /// <param name="version">The nuget version string</param>
        /// <returns>A .NET version number</returns>
        private static Version ParseVersion(string version)
        {
            var v = new Version(version.Split("-")[0]);
            return new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
        }

        /// <summary>
        /// A version that is duplicated in multiple projects
        /// </summary>
        /// <param name="Project">The source project</param>
        /// <param name="Version">The resolved nuget version string</param>
        /// <param name="ParsedVersion">The resolved parsed version</param>
        public sealed record DuplicatedVersion(
            string Project,
            string Version,
            Version ParsedVersion
        );

        /// <summary>
        /// Parses the output of the dotnet list command and returns a dictionary of duplicated versions
        /// </summary>
        /// <param name="input">The parsed output from the dotnet list command</param>
        /// <returns>A dictionary of duplicated versions, where the key is the package id and the value is a list of projects that use that version</returns>
        public static Dictionary<string, List<DuplicatedVersion>> GetDuplicatedVersions(RootJson input)
            => input.Projects
                .SelectMany(x => x.Frameworks.Select(y => new
                {
                    Framework = y,
                    Project = x.Path
                }))
                .SelectMany(x =>
                    (x.Framework.TopLevelPackages?
                        .Select(y => new
                        {
                            TopLevel = true,
                            y.Id,
                            y.ResolvedVersion,
                            x.Project
                        }) ?? [])
                        .Concat(x.Framework.TransitivePackages?.Select(y => new { TopLevel = false, y.Id, y.ResolvedVersion, x.Project }) ?? [])
                )
                .GroupBy(x => x.Id, x => new DuplicatedVersion(x.Project, x.ResolvedVersion, ParseVersion(x.ResolvedVersion)))
                .Where(x => x.DistinctBy(y => y.ParsedVersion).Count() > 1)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToList()
                );

        /// <summary>
        /// Finds the maximum nuget versions of packages
        /// </summary>
        /// <param name="input">The parsed output from the dotnet list command</param>
        /// <returns>A list of nuget versions for each package</returns>
        public static Dictionary<string, Version> FindMaxNugetVersions(RootJson input)
            => input.Projects
                .SelectMany(x => x.Frameworks)
                .SelectMany(x =>
                    (x.TopLevelPackages?
                        .Select(x => new { TopLevel = true, x.Id, x.ResolvedVersion }) ?? [])
                        .Concat(x.TransitivePackages?.Select(x => new { TopLevel = false, x.Id, x.ResolvedVersion }) ?? [])
                )
                .Where(x => !x.TopLevel)
                .GroupBy(x => x.Id, x => x.ResolvedVersion)
                .Select(x => new { x.Key, Version = x.MaxBy(ParseVersion) })
                .ToDictionary(
                    x => x.Key,
                    x => ParseVersion(x.Version!)
                );

        /// <summary>
        /// List of known wrong versions, where the assembly version is not the same as the nuget version
        /// </summary>
        private static Dictionary<string, Version> ManuallyFixedVersions = new Dictionary<string, Version>
        {
            // Using v4.0 for assembly, but 4.0.6.4 in nuget
            { "AWSSDK.Core", new Version(4, 0, 0, 0) },

            // Using the Framework version, not the package version
            { "Microsoft.CSharp", new Version(8, 0, 0, 0) },
            { "System.Memory", new Version(8, 0, 0, 0) },
            { "System.Security.AccessControl", new Version(8, 0, 0, 0) },
            { "System.Security.Principal.Windows", new Version(8, 0, 0, 0) },
            { "System.Security.Cryptography.Algorithms", new Version(8, 0, 0, 0) },
            { "System.Security.Cryptography.Cng", new Version(8, 0, 0, 0) },

            // The assembly version also has a revision number, but the nuget version does not.
            { "SQLitePCLRaw.core", new Version(2, 1, 10, 2445) },

            // Using v9.0 for assembly, but 9.0.2 in nuget
            { "System.IO.Pipelines", new Version(9, 0, 0, 0) },

            // Using v9.0 for assembly, but 9.0.6 in nuget
            { "Microsoft.Win32.SystemEvents", new Version(10, 0, 0, 0) },
            { "System.Drawing.Common", new Version(10, 0, 0, 0) },

            // Using v6.0.0.1 for assembly, but 6.0.1 in nuget
            { "System.Memory.Data", new Version(6, 0, 0, 1) }
        };

        /// <summary>
        /// Verifies that the versions of the assemblies in the output folder are the maximum versions
        /// </summary>
        /// <param name="folder">The folder to check</param>
        /// <param name="input">The parsed output from the dotnet list command</param>
        /// <param name="allowAssemblyMismatch">If true, allows mismatches between the assembly version and the nuget version</param>
        /// <returns>An awaitable task</returns>
        public static Task VerifyDuplicatedVersionsAreMaxVersions(string folder, RootJson input, bool allowAssemblyMismatch)
        {
            var duplicatedVersions = GetDuplicatedVersions(input)
                .Select(x =>
                {
                    if (ManuallyFixedVersions.TryGetValue(x.Key, out var version))
                        return new KeyValuePair<string, List<DuplicatedVersion>>(x.Key, [new DuplicatedVersion(x.Value.First().Project, x.Value.First().Version, version)]);
                    return new KeyValuePair<string, List<DuplicatedVersion>>(x.Key, x.Value);
                });

            var mismatches = new List<(string Path, Version Expected, Version Actual)>();
            foreach (var entry in duplicatedVersions)
            {
                var maxVersion = entry.Value.MaxBy(x => x.ParsedVersion)
                    ?? throw new Exception($"Failed to find max version for {entry.Key}");
                var filename = Path.Combine(folder, $"{entry.Key}.dll");
                if (!File.Exists(filename))
                    continue;

                var assemblyVersion = AssemblyName.GetAssemblyName(filename).Version;
                if (assemblyVersion != null && assemblyVersion != maxVersion.ParsedVersion)
                    mismatches.Add((filename, maxVersion.ParsedVersion, assemblyVersion));
            }

            if (mismatches.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var mismatch in mismatches)
                    sb.AppendLine($"File {mismatch.Path} has version {mismatch.Actual} but expected {mismatch.Expected}");
                if (!allowAssemblyMismatch)
                    throw new Exception(sb.ToString());
            }

            return Task.CompletedTask;
        }
    }
}