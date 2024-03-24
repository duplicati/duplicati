namespace ReleaseBuilder.CliCommand;

public static partial class Build
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
        /// <returns>An awaitable task</returns>
        public static Task MakeSymlinks(string buildDir)
        {
            foreach (var k in ExecutableRenames)
                if (File.Exists(Path.Combine(buildDir, k.Key)) && !File.Exists(Path.Combine(buildDir, k.Value)))
                    File.CreateSymbolicLink(Path.Combine(buildDir, k.Value), Path.Combine(".", k.Key));

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
                var executables = rtcfg.ExecutableBinaries.Select(x => Path.Combine(buildDir, x))
                    .Concat(Directory.EnumerateFiles(buildDir, "*.sh", SearchOption.AllDirectories));
                var filemode = EnvHelper.GetUnixFileMode("+x");
                foreach (var x in executables)
                    if (File.Exists(x))
                        EnvHelper.AddFilemode(x, filemode);
            }

            return Task.CompletedTask;
        }
    }
}