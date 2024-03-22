
using System.CommandLine;

namespace ReleaseBuilder;

/// <summary>
/// Entry point for the executable
/// </summary>
class Program
{
    /// <summary>
    /// The supported build packages
    /// </summary>
    public static readonly IReadOnlyList<PackageTarget> SupportedPackageTargets = new[] {
        "win-x64-gui.zip",
        "win-x64-gui.msi",
        "win-x86-gui.zip",
        "win-x86-gui.msi",
        "win-arm64-gui.zip",
        "win-arm64-gui.msi",
        "linux-x64-gui.zip",
        "linux-x64-gui.deb",
        "linux-x64-gui.rpm",
        "linux-x64-cli.docker",
        // "linux-x64-cli.spk",
        "linux-arm64-cli.docker",
        "linux-arm64-gui.zip",
        "linux-arm64-gui.deb",
        "linux-arm64-gui.rpm",
        // "linux-arm64-cli.spk",
        "osx-x64-gui.dmg",
        "osx-x64-gui.pkg",
        "osx-arm64-gui.dmg",
        "osx-arm64-gui.pkg",
    }
    .Select(x => PackageTarget.ParsePackageId(x))
    .Distinct()
    .ToList();

    /// <summary>
    /// The environment shared configuration
    /// </summary>
    public static readonly Configuration Configuration = Configuration.Create();

    /// <summary>
    /// Invokes the builder
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    static Task<int> Main(string[] args)
        => new RootCommand("Build tool for Duplicati")
        {
            CliCommand.Build.Create()
        }.InvokeAsync(args);
}