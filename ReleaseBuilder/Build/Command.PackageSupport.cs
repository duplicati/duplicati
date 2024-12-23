namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Support for building packages
    /// </summary>
    private static class PackageSupport
    {
        /// <summary>
        /// Sets the executable flags for the build output
        /// </summary>
        /// <param name="srcdir">The build directory</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static Task SetPermissionFlags(string srcdir, RuntimeConfig rtcfg)
        {
            if (OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Setting executable flags is not supported on Windows, use WSL or Docker");

            var roflags = UnixFileMode.OtherRead | UnixFileMode.GroupRead | UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var exflags = roflags | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

            var exenames = ExecutableRenames.Values.Concat(ExecutableRenames.Keys).ToHashSet();

            // Mark executables with the execute flag
            var executables = ExecutableRenames.Values.Select(x => Path.Combine(srcdir, x))
                .Concat(ExecutableRenames.Keys.Select(x => Path.Combine(srcdir, x)))
                .Concat(Directory.EnumerateFiles(srcdir, "*.sh", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(srcdir, "*.py", SearchOption.AllDirectories))
                .ToHashSet();

            foreach (var f in Directory.EnumerateFileSystemEntries(srcdir, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(f))
                    File.SetUnixFileMode(f,
                        executables.Contains(f)
                            ? exflags
                            : roflags);
                else if (Directory.Exists(f))
                    File.SetUnixFileMode(f, exflags);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Renames all executables to their shorter form
        /// </summary>
        /// <param name="buildDir"></param>
        /// <returns>An awaitable task</returns>
        public static Task RenameExecutables(string buildDir)
        {
            foreach (var exefile in ExecutableRenames.Keys)
            {
                var targetfile = Path.Combine(buildDir, exefile);
                if (File.Exists(targetfile))
                    File.Move(targetfile, Path.Combine(buildDir, ExecutableRenames[exefile]));
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Writes the package type identifier to the build directory
        /// </summary>
        /// <param name="buildDir">The build directory to update</param>
        /// <param name="target">The target configuration</param>
        /// <returns>An awaitable task</returns>
        public static Task InstallPackageIdentifier(string buildDir, PackageTarget target)
            => File.WriteAllTextAsync(Path.Combine(buildDir, "package_type_id.txt"), target.PackageTargetString);

        /// <summary>
        /// Codesigns the binary files in the given directory
        /// </summary>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <param name="binDir">The directory containing the binaries</param>
        /// <param name="entitlementFile">The entitlement file to use</param>
        /// <returns>An awaitable task</returns>
        public static async Task SignMacOSBinaries(RuntimeConfig rtcfg, string binDir, string entitlementFile)
        {
            if (rtcfg.UseCodeSignSigning)
            {
                Console.WriteLine("Performing MacOS code signing ...");

                // Executables cannot be signed before their dependencies are signed
                // So they are placed last in the list
                var executables = ExecutableRenames.Values.Select(x => Path.Combine(binDir, x))
                    .Where(File.Exists);

                var signtargets = Directory.EnumerateFiles(binDir, "*", SearchOption.AllDirectories)
                    .Except(executables)
                    .Concat(executables)
                    .Distinct()
                    .ToList();

                foreach (var f in signtargets)
                    await rtcfg.Codesign(f, entitlementFile);
            }
        }
    }
}