namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Support for building packages
    /// </summary>
    private static class PackageSupport
    {
        /// <summary>
        /// Introduces symbolic links for executables that have a different name
        /// </summary>
        /// <param name="buildDir">The build path to use</param>
        /// <param name="targetDir">The target directory to create the symlinks in</param>
        /// <returns>An awaitable task</returns>
        public static Task MakeSymlinks(string buildDir, string? targetDir = null)
        {
            foreach (var k in ExecutableRenames)
                if (File.Exists(Path.Combine(buildDir, k.Key)) && !File.Exists(Path.Combine(buildDir, k.Value)))
                    File.CreateSymbolicLink(Path.Combine(buildDir, k.Value), Path.Combine(targetDir ?? ".", k.Key));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sets the executable flags for the build output
        /// </summary>
        /// <param name="buildDir">The build directory</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static Task SetExecutableFlags(string buildDir, RuntimeConfig rtcfg)
        {
            if (!OperatingSystem.IsWindows())
            {
                // Mark executables with the execute flag
                var executables = ExecutableRenames.Keys.Select(x => Path.Combine(buildDir, x))
                    .Concat(Directory.EnumerateFiles(buildDir, "*.sh", SearchOption.AllDirectories));
                var filemode = EnvHelper.GetUnixFileMode("+x");
                foreach (var x in executables)
                    if (File.Exists(x))
                        EnvHelper.AddFilemode(x, filemode);
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