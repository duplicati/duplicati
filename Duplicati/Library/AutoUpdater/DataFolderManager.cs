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

#nullable enable

using System;
using System.IO;
using System.Linq;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Manages the data folder for the application
/// </summary>
public static class DataFolderManager
{
    public enum AccessMode
    {
        ProbeOnly,
        ReadWritePermissionSet
    }
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(DataFolderManager));

    /// <summary>
    /// The installation ID filename stored in <see cref="DATAFOLDER"/>
    /// </summary>
    private const string INSTALL_FILE = "installation.txt";

    /// <summary>
    /// The machine ID filename stored in <see cref="DATAFOLDER"/>
    /// </summary>
    private const string MACHINE_FILE = "machineid.txt";

    /// <summary>
    /// The option name for portable mode
    /// </summary>
    public const string PORTABLE_MODE_OPTION = "portable-mode";
    /// <summary>
    /// The option anme for the server data folder
    /// </summary>
    public const string SERVER_DATAFOLDER_OPTION = "server-datafolder";

    /// <summary>
    /// The app name to use for variables
    /// </summary>
    private static readonly string APPNAME = AutoUpdateSettings.AppName;

    /// <summary>
    /// The name of the environment variable that allows overriding the path to the data folder used by Duplicati
    /// </summary>
    public static readonly string DATAFOLDER_ENV_NAME = $"{APPNAME}_HOME".ToUpperInvariant();

    /// <summary>
    /// Name of the database file
    /// </summary>
    public static readonly string SERVER_DATABASE_FILENAME = $"{APPNAME}-server.sqlite";

    /// <summary>
    /// Flag to indicate if the application is running in portable mode
    /// </summary>
    public static bool PORTABLE_MODE { private set; get; }

    /// <summary>
    /// Flag to indicate if the data folder was overriden
    /// </summary>
    public static bool OVERRIDEN_DATAFOLDER { private set; get; }

    /// <summary>
    /// The folder where the machine id is placed
    /// </summary>
    public static string GetDataFolder(AccessMode mode)
    {
        // Trigger portable mode, if the flag is set
        PORTABLE_MODE = ParseBoolSlim(ExtractOptionSlim(PORTABLE_MODE_OPTION));

        string dataFolder = string.Empty;

        // The environment variable is a legacy setting
        var envOverride = Environment.GetEnvironmentVariable(DATAFOLDER_ENV_NAME);

        // These are mainly supported by the Server
        var datafolderArg = ExtractOptionSlim(SERVER_DATAFOLDER_OPTION);

        // Prefer the command line argument over the environment variable
        if (!string.IsNullOrWhiteSpace(datafolderArg))
        {
            OVERRIDEN_DATAFOLDER = true;
            dataFolder = Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(datafolderArg).Trim('"'));
        }
        // Portable mode is prefered over the environment variable
        else if (PORTABLE_MODE)
        {
            OVERRIDEN_DATAFOLDER = true;
            dataFolder = Util.AppendDirSeparator(Path.Combine(UpdaterManager.INSTALLATIONDIR, "data"));
        }
        // Use the legacy environment variable, if set
        else if (!string.IsNullOrWhiteSpace(envOverride))
        {
            OVERRIDEN_DATAFOLDER = true;
            dataFolder = Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(envOverride).Trim('"'));
        }
        // Use the default location
        else
        {
            OVERRIDEN_DATAFOLDER = false;
            dataFolder = Util.AppendDirSeparator(DataFolderLocator.GetDefaultStorageFolderInternal(SERVER_DATABASE_FILENAME, APPNAME));
        }

        if (Directory.Exists(dataFolder))
        {
            if (!File.Exists(Path.Combine(dataFolder, Util.InsecurePermissionsMarkerFile)))
                try
                {
                    if (mode == AccessMode.ReadWritePermissionSet)
                        SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dataFolder);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToSetPermissions", ex, "Failed to set permissions for {0}: {1}", dataFolder, ex.Message);
                }
        }
        else if (mode == AccessMode.ReadWritePermissionSet)
        {
            try
            {
                Directory.CreateDirectory(dataFolder);
                try
                {
                    SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dataFolder);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToSetPermissions", ex, "Failed to set permissions for {0}: {1}", dataFolder, ex.Message);
                }
            }
            catch (Exception ex)
            {
                throw new UserInformationException($"Failed to create data folder {dataFolder}", "FailedToCreateDataFolder", ex);
            }
        }

        if (mode == AccessMode.ReadWritePermissionSet && !File.Exists(Path.Combine(dataFolder, INSTALL_FILE)))
        {
            // In case there was already a machine id file from 2.0.8.1 or older, copy it to the new location
            if (File.Exists(Path.Combine(dataFolder, "updates", INSTALL_FILE)))
                File.Copy(Path.Combine(dataFolder, "updates", INSTALL_FILE), Path.Combine(dataFolder, INSTALL_FILE), true);
            else
                File.WriteAllText(Path.Combine(dataFolder, INSTALL_FILE), AutoUpdateSettings.UpdateInstallFileText);
        }

        if (mode == AccessMode.ReadWritePermissionSet && !File.Exists(Path.Combine(dataFolder, MACHINE_FILE)))
            File.WriteAllText(Path.Combine(dataFolder, MACHINE_FILE), AutoUpdateSettings.UpdateMachineFileText(InstallID));

        return dataFolder;
    }

    /// <summary>
    /// Replication of the argument parsing from the main Duplicati codebase
    /// </summary>
    /// <param name="option">The option to extract</param>
    /// <returns><c>null</c> if the option is not found, otherwise the value of the option</returns>
    private static string? ExtractOptionSlim(string option)
    {
        var opt = $"--{option}";
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var match = args.Select((token, index) => new { token, index })
            .LastOrDefault(x =>
                string.Equals(x.token, opt, StringComparison.OrdinalIgnoreCase)
                ||
                x.token.StartsWith(opt + "=", StringComparison.OrdinalIgnoreCase)
            );

        // Not found, try the environment variable
        if (string.IsNullOrWhiteSpace(match?.token))
            return Environment.GetEnvironmentVariable($"{AutoUpdateSettings.AppName}__{option.Replace('-', '_')}".ToUpperInvariant());

        // Found in the form --option=value
        if (match.token.StartsWith(opt + "=", StringComparison.OrdinalIgnoreCase))
            return match.token.Substring(opt.Length + 1).Trim('"');

        // Found in the form --option value
        if (match.index + 1 < args.Length)
        {
            var value = args[match.index + 1];
            if (!value.StartsWith("--"))
                return value;
        }

        // Found, but no value, just the option
        return "";
    }

    /// <summary>
    /// Replication of the boolean parsing from the main Duplicati codebase
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <returns><c>true</c> if the value is a truthy value, otherwise <c>false</c></returns>
    private static bool ParseBoolSlim(string? value)
    {
        // In debug builds, we default to portable mode
        if (value == null)
#if DEBUG
            return true;
#else
            return false;
#endif

        return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The unique machine installation ID
    /// </summary>
    public static string InstallID => _installID.Value;

    /// <summary>
    /// The unique machine ID, lazy evaluated
    /// </summary>
    private static readonly Lazy<string> _installID = new(() =>
    {
        try
        {
            var path = Path.Combine(GetDataFolder(DataFolderManager.AccessMode.ProbeOnly), INSTALL_FILE);
            if (File.Exists(path))
                return File.ReadAllLines(path).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
        }
        catch { }

        return string.Empty;
    });

    /// <summary>
    /// The unique machine ID
    /// </summary>
    public static string MachineID => _machineID.Value;

    /// <summary>
    /// The unique machine ID, lazy evaluated
    /// </summary>
    private static readonly Lazy<string> _machineID = new(() =>
    {
        string? machinedId = null;
        try { machinedId = File.ReadAllLines(Path.Combine(GetDataFolder(DataFolderManager.AccessMode.ProbeOnly), MACHINE_FILE)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? ""; }
        catch { }

        return string.IsNullOrWhiteSpace(machinedId)
            ? InstallID
            : machinedId;
    });

    /// <summary>
    /// The machine name, lazy evaluated
    /// </summary>
    private static readonly Lazy<string> _machineName = new(MachineNameReader.GetMachineName);

    /// <summary>
    /// The machine name
    /// </summary>
    public static readonly string MachineName = _machineName.Value;

}