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
    }
}