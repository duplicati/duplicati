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