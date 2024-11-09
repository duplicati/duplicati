
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
        "win-x64-agent.msi", // Missing window service support
        "win-arm64-agent.msi", // Missing window service support
        "win-x64-agent.zip",
        "win-arm64-agent.zip",

        "linux-x64-gui.zip",
        "linux-x64-gui.deb",
        "linux-x64-gui.rpm",
        "linux-x64-cli.zip",
        "linux-x64-cli.deb",
        "linux-x64-cli.rpm",
        "linux-x64-cli.docker",
        // "linux-x64-cli.spk", // Need to add new integration with DSM WebUI
        "linux-x64-agent.zip",
        "linux-x64-agent.deb",
        "linux-x64-agent.rpm",
        "linux-x64-agent.docker",

        "linux-arm7-gui.zip",
        "linux-arm7-gui.deb",
        "linux-arm7-cli.zip",
        "linux-arm7-cli.deb",
        "linux-arm7-cli.docker",

        "linux-arm64-gui.zip",
        "linux-arm64-gui.deb",
        "linux-arm64-gui.rpm",
        "linux-arm64-cli.zip",
        "linux-arm64-cli.deb",
        "linux-arm64-cli.rpm",
        "linux-arm64-cli.docker",
        // "linux-arm64-cli.spk",  // Need to add new integration with DSM WebUI
        "linux-arm64-agent.zip",
        "linux-arm64-agent.deb",
        "linux-arm64-agent.rpm",
        "linux-arm64-agent.docker",

        "osx-x64-gui.dmg",
        "osx-x64-gui.pkg",
        "osx-x64-agent.pkg",
        "osx-x64-cli.pkg",
        "osx-arm64-gui.dmg",
        "osx-arm64-gui.pkg",
        "osx-arm64-agent.pkg",
        "osx-arm64-cli.pkg",
    }
    .Select(x => PackageTarget.ParsePackageId(x))
    .Distinct()
    .ToList();

    /// <summary>
    /// The environment shared configuration
    /// </summary>
    public static readonly Configuration Configuration = Configuration.Create();

    /// <summary>
    /// The return code of the application; shared state
    /// </summary>
    public static int? ReturnCode = 0;

    /// <summary>
    /// Invokes the builder
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    static async Task<int> Main(string[] args)
    {
        var r = await new RootCommand("Build tool for Duplicati")
        {
            Build.Command.Create(),
            CreateKey.Command.Create(),
        }.InvokeAsync(args);

        return ReturnCode ?? r;
    }
}