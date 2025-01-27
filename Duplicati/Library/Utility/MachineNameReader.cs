using System;
using System.Management;
using System.Runtime.Versioning;

#nullable enable

namespace Duplicati.Library.Utility;

/// <summary>
/// Class for reading the machine name
/// </summary>
public static class MachineNameReader
{
    /// <summary>
    /// Makes a best effort to get the machine name
    /// </summary>
    /// <returns>The machine name</returns>
    public static string GetMachineName()
    {
        string? machineName = null;
        if (OperatingSystem.IsWindows())
            machineName = GetMachineNameWindows();
        else if (OperatingSystem.IsMacOS())
            machineName = GetMachineNameMacOS();
        else if (OperatingSystem.IsLinux())
            machineName = GetMachineNameLinux();

        return string.IsNullOrWhiteSpace(machineName)
            ? Environment.MachineName
            : machineName;
    }
    /// <summary>
    /// Executes a command and reads the output
    /// </summary>
    /// <param name="command">The command to execute</param>
    /// <param name="arguments">The arguments to pass to the command</param>
    /// <returns>The output of the command</returns>
    private static string ExecuteAndReadOutput(string command, string arguments)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(TimeSpan.FromSeconds(1));
            if (!process.HasExited)
            {
                process.Kill();
                return string.Empty;
            }

            if (process.ExitCode != 0)
                return string.Empty;

            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the machine name if running MacOS
    /// </summary>
    /// <returns>The machine name</returns>
    [SupportedOSPlatform("macos")]
    private static string? GetMachineNameMacOS()
        => ExecuteAndReadOutput("scutil", "--get ComputerName");

    /// <summary>
    /// Gets the machine name if running Windows
    /// </summary>
    /// <returns>The machine name</returns>
    [SupportedOSPlatform("windows")]
    private static string? GetMachineNameWindows()
        => null; // No special handling for Windows, always uses NetBIOS name

    /// <summary>
    /// Gets the machine name if running Linux
    /// </summary>
    /// <returns>The machine name</returns>
    [SupportedOSPlatform("linux")]
    private static string? GetMachineNameLinux()
        => null; // No special handling for Linux, always uses hostname
}
