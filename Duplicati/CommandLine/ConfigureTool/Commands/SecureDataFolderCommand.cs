// Copyright (C) 2026, The Duplicati Team
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
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;

namespace Duplicati.CommandLine.ConfigureTool.Commands;

/// <summary>
/// Command for securing the permissions on the Duplicati data folder.
/// </summary>
public static class SecureDataFolderCommand
{
    /// <summary>
    /// Creates the 'secure-datafolder' command.
    /// </summary>
    public static Command CreateSecureDataFolderCommand()
    {
        var cmd = new Command("secure-datafolder", OperatingSystem.IsWindows()
            ? "Restrict the permissions on the data folder so only the current user, SYSTEM and Administrators can access it"
            : "Restrict the permissions on the data folder so only root and the current user can access it")
        {
            new Option<string>("--datafolder", "Path to the Duplicati data folder (defaults to standard location)"),
            new Option<bool>("--apply", "Apply the restricted permissions without prompting. By default a warning is shown and the user must confirm."),
            new Option<bool>("--for-service", "Apply the restricted permissions for use with a service"),
            new Option<bool>("--quiet", "Suppress all output except for minimum messages, such as errors"),
        };

        cmd.Handler = CommandHandler.Create<string?, bool, bool, bool>(HandleSecureDataFolder);
        return cmd;
    }

    /// <summary>
    /// Gets the data folder path, either from the option or using the default.
    /// </summary>
    private static string GetDataFolder(string? dataFolderOption)
    {
        if (!string.IsNullOrWhiteSpace(dataFolderOption))
            return Path.GetFullPath(dataFolderOption);

        return DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);
    }

    /// <summary>
    /// Captures log messages generated in the library and forwards them to the console.
    /// </summary>
    private static IDisposable StartConsoleLogScope()
        => Log.StartScope(entry =>
        {
            if (entry.Level == LogMessageType.Information)
                Console.WriteLine(entry.FormattedMessage);
            else
                Console.WriteLine($"{entry.Level}: {entry.FormattedMessage}");
        }, entry => entry.Level >= LogMessageType.Information);

    /// <summary>
    /// Handles the 'secure-datafolder' command.
    /// Checks the current permissions on the data folder and, if they are not already restricted,
    /// applies the restricted permissions (current user, SYSTEM and Administrators only).
    /// </summary>
    private static int HandleSecureDataFolder(string? datafolder, bool apply, bool forService, bool quiet)
    {
        var dataFolderPath = GetDataFolder(datafolder);

        using var _ = StartConsoleLogScope();
        if (!quiet)
            Console.WriteLine($"Using data folder: {dataFolderPath}");

        if (!Directory.Exists(dataFolderPath))
        {
            Console.WriteLine($"The data folder does not exist: {dataFolderPath}");
            if (!quiet)
                Console.WriteLine("Create the folder first by starting the server or by creating it manually, then re-run this command.");
            return 1;
        }

        // Check if the permissions are already set as expected
        var alreadySecure = SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dataFolderPath, forService, out var detail);

        if (alreadySecure)
        {
            if (!quiet)
            {
                Console.WriteLine("The data folder already has restricted permissions (only the current user, SYSTEM and Administrators can access it).");
                Console.WriteLine("No changes are needed.");
            }
            return 0;
        }

        if (!quiet)
        {
            Console.WriteLine("The data folder does not have restricted permissions.");
            Console.WriteLine($"Current state: {detail}");
            Console.WriteLine();
            Console.WriteLine("Warning: if someone created this folder, running this command may inadvertently give unwanted access to this system.");
            Console.WriteLine("Before proceeding, verify that the data folder was created by Duplicati and not by an attacker,");
            Console.WriteLine("as restricting the permissions will grant the current user, SYSTEM and Administrators full control over the folder.");
            Console.WriteLine();
        }

        if (!apply)
        {
            Console.Write("Apply restricted permissions? [y/N] ");
            var response = Console.ReadLine();
            if (!string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                if (!quiet)
                    Console.WriteLine("Aborted. No changes were made.");
                return 1;
            }
        }

        try
        {
            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dataFolderPath, forService);
            if (!SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dataFolderPath, forService, out var checkDetail))
            {
                Console.WriteLine($"Warning: failed to verify that permissions were applied correctly: {checkDetail}");
                return 1;
            }

            if (!quiet)
            {
                Console.WriteLine("Restricted permissions applied to the data folder.");
                Console.WriteLine("Only the current user, SYSTEM and Administrators can now access the folder.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set permissions on the data folder: {ex.Message}");
            return 1;
        }
    }
}
