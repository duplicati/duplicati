
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
        "win-x64.zip",
        "win-x64.msi",
        "win-x86.zip",
        "win-x86.msi",
        "win-arm64.zip",
        "win-arm64.msi",
        "linux-x64.zip",
        "linux-x64.deb",
        "linux-x64.rpm",
        "linux-x64.docker",
        "linux-arm64.docker",
        "linux-arm64.zip",
        "linux-arm64.deb",
        "linux-arm64.rpm",
        "linux-arm64.syno",
        "osx-x64.dmg",
        "osx-x64.pkg",
        "osx-arm64.dmg",
        "osx-arm64.pkg",
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