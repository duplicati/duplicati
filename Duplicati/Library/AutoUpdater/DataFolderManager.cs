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
    /// The option name for allowing an insecure data folder.
    /// When set, the data folder permission check is skipped, allowing the data folder to be
    /// placed in a shared location without restricting its permissions. The value is resolved
    /// by <see cref="Util.AllowInsecureDataFolder"/>, which also honors the
    /// <c>DUPLICATI__ALLOW_INSECURE_DATAFOLDER</c> environment variable.
    /// </summary>
    public const string ALLOW_INSECURE_DATAFOLDER_OPTION = Util.AllowInsecureDatafolderOption;

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

        // Verify the folder security (folder-squatting / seeded-content protection) and,
        // for a folder we create ourselves, lock it down. This is a no-op in ProbeOnly mode.
        if (mode == AccessMode.ReadWritePermissionSet)
            PrepareSecureDataFolder(dataFolder, createIfMissing: true);

        if (mode == AccessMode.ReadWritePermissionSet && !File.Exists(Path.Combine(dataFolder, INSTALL_FILE)))
        {
            // In case there was already a machine id file from 2.0.8.1 or older, copy it to the new location
            if (File.Exists(Path.Combine(dataFolder, "updates", INSTALL_FILE)))
                File.Copy(Path.Combine(dataFolder, "updates", INSTALL_FILE), Path.Combine(dataFolder, INSTALL_FILE), true);
            else
                File.WriteAllText(Path.Combine(dataFolder, INSTALL_FILE), AutoUpdateSettings.UpdateInstallFileText);
        }

        if (mode == AccessMode.ReadWritePermissionSet && !File.Exists(Path.Combine(dataFolder, MACHINE_FILE)))
            File.WriteAllText(Path.Combine(dataFolder, MACHINE_FILE), AutoUpdateSettings.UpdateMachineFileText(GetInstallID()));

        return dataFolder;
    }

    /// <summary>
    /// Prepares a data folder for secure use.
    ///
    /// The rules are:
    /// <list type="bullet">
    /// <item>A folder that this call creates is always locked down to the current user, SYSTEM
    /// and Administrators, and then verified.</item>
    /// <item>A pre-existing folder is never modified. Its permissions are verified as-is: if
    /// they are already in the canonical locked-down form the folder is accepted; otherwise it
    /// is rejected, because Duplicati does not adopt or "heal" a folder it did not create (an
    /// attacker could have created it and seeded malicious content).</item>
    /// </list>
    ///
    /// The decision is based on the folder's actual ACL/ownership, not on its path, so
    /// operator-overridden locations (via <c>--{SERVER_DATAFOLDER_OPTION}</c>,
    /// <c>{DATAFOLDER_ENV_NAME}</c> or portable mode) are protected just like the default
    /// location, on all platforms.
    ///
    /// If a pre-existing folder is not canonical, the operator can either run the
    /// 'configuretool secure-datafolder' command to restrict its permissions, or pass
    /// <c>--{ALLOW_INSECURE_DATAFOLDER_OPTION}</c> to bypass the check.
    /// </summary>
    /// <param name="dataFolder">The resolved data folder path.</param>
    /// <param name="createIfMissing">Whether to create the folder if it does not exist.</param>
    public static void PrepareSecureDataFolder(string dataFolder, bool createIfMissing)
    {
        var folderPreExisted = Directory.Exists(dataFolder);

        if (!folderPreExisted)
        {
            if (!createIfMissing)
                return;

            try
            {
                Directory.CreateDirectory(dataFolder);
            }
            catch (Exception ex)
            {
                throw new UserInformationException($"Failed to create data folder {dataFolder}", "FailedToCreateDataFolder", ex);
            }

            // A folder we just created is always locked down, regardless of the opt-out: it is
            // ours, so there is no reason to leave it accessible. On a filesystem that does not
            // support restrictive permissions the lockdown is a best-effort no-op (logged as a
            // warning), and the opt-out below governs whether that is acceptable.
            TryLockDownFolder(dataFolder);
        }

        // When the operator has explicitly opted in to an insecure data folder, skip the
        // verification entirely. This exists for deliberately shared folders or filesystems
        // that do not support restrictive permissions (e.g. FAT32, some network mounts).
        if (Util.AllowInsecureDataFolder())
            return;

        // Verify the permissions. A folder we created is now canonical and passes. A pre-existing
        // folder is accepted only if it is already canonical; otherwise it is rejected without
        // being modified.
        VerifyDataFolderSecurity(dataFolder, folderPreExisted);
    }

    /// <summary>
    /// Verifies that an existing data folder has the canonical restricted permissions, without
    /// creating or modifying it. Used by read-only callers that must not change the folder but
    /// still need the folder-squatting / seeded-content protection. Throws a
    /// <see cref="UserInformationException"/> if the folder is not secure and the user has not
    /// opted in with <c>--allow-insecure-datafolder</c>.
    /// </summary>
    /// <param name="dataFolder">The resolved data folder path.</param>
    public static void VerifyDataFolderSecurityReadOnly(string dataFolder)
    {
        if (Util.AllowInsecureDataFolder())
            return;

        VerifyDataFolderSecurity(dataFolder, folderPreExisted: true);
    }

    /// <summary>
    /// Attempts to restrict the folder permissions to the current user, SYSTEM and
    /// Administrators, logging a warning on failure rather than throwing. Failure is not fatal
    /// here; the subsequent verification decides whether an insecure folder is acceptable.
    /// </summary>
    /// <param name="dataFolder">The folder to lock down.</param>
    private static void TryLockDownFolder(string dataFolder)
    {
        try
        {
            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dataFolder, false);
        }
        catch (Exception ex)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "FailedToSetPermissions", ex, "Failed to set permissions for {0}: {1}", dataFolder, ex.Message);
        }
    }

    /// <summary>
    /// Verifies that the data folder has the canonical restricted permissions (only the current
    /// user, SYSTEM and Administrators, with inheritance disabled and a trusted owner). The
    /// decision is based on the folder's actual ACL/ownership rather than its path, so any
    /// world-writable or attacker-owned folder is caught regardless of where it is located.
    ///
    /// The folder is never modified here; it is only inspected. A folder that this process just
    /// created has already been locked down and passes; a pre-existing folder is accepted only
    /// if it is already canonical and is otherwise refused (unless the user opts in with
    /// <c>--allow-insecure-datafolder</c>).
    /// </summary>
    /// <param name="dataFolder">The resolved data folder path.</param>
    /// <param name="folderPreExisted">
    /// <c>true</c> if the folder already existed before this run (potential squatting target),
    /// <c>false</c> if it was just created by this process.
    /// </param>
    private static void VerifyDataFolderSecurity(string dataFolder, bool folderPreExisted)
    {
        // The folder must exist for there to be anything to verify.
        if (!Directory.Exists(dataFolder))
            return;

        // The folder is acceptable only if it is in the canonical locked-down form.
        if (SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dataFolder, false, out var detail))
            return;

        var configuretoolname = PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.ConfigureTool);

        // Tailor the guidance based on whether the folder pre-existed. A pre-existing folder
        // that is not canonical is either an attacker-created folder (squatting) or one whose
        // permissions have been changed; either way Duplicati will not adopt it and requires
        // the operator to explicitly secure it or vouch for it.
        var guidance = folderPreExisted
            ? "This folder already existed before Duplicati started and does not have the expected restricted permissions, " +
              "which can mean it was created by another (possibly malicious) user and may contain a malicious database or configuration. " +
              "Duplicati does not automatically adopt an existing folder's permissions. Verify the folder was created by Duplicati " +
              $"(not an attacker), then restrict its permissions using the '{configuretoolname} secure-datafolder' command (or manually). "
            : $"Restrict the permissions on the folder using the '{configuretoolname} secure-datafolder' command (or manually). ";

        throw new UserInformationException(
            $"The data folder '{dataFolder}' does not have secure permissions ({detail}). " +
            $"This is a security risk because the database and configuration may contain sensitive data, " +
            $"and an attacker who can write to the folder could inject a malicious database or configuration. " +
            guidance +
            $"Alternatively, run with --{Util.AllowInsecureDatafolderOption} to bypass this check if you understand the risks.",
            "InsecureDataFolderPermissions");
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
    /// The cached unique machine installation ID
    /// </summary>
    private static string? _installID;

    /// <summary>
    /// The cached unique machine ID
    /// </summary>
    private static string? _machineID;

    /// <summary>
    /// The unique machine installation ID.
    /// The value is read from the installation file in the data folder. If the file does not
    /// exist yet, an empty value is returned without caching, so a later call can still pick
    /// up the value; once the file exists, the value is stable and is cached, even if empty.
    /// </summary>
    /// <param name="probeOnly">A flag indicating the caller is only probing; a missing ID returns empty without throwing (debug) or logging</param>
    /// <returns>The ID or an empty string</returns>
    public static string GetInstallID(bool probeOnly = false)
    {
        if (_installID == null)
        {
            var value = ProbeIdFile(INSTALL_FILE, out var fileExists);
            if (!fileExists)
                return ReportMissingIdValue(INSTALL_FILE, probeOnly);

            _installID = value ?? "";
        }

        return _installID;
    }

    /// <summary>
    /// The unique machine ID.
    /// The value is read from the machine id file in the data folder, falling back to the
    /// installation ID. If the machine id file does not exist yet and no value was found,
    /// an empty value is returned without caching, so a later call can still pick up the
    /// value; once the file exists, the value is stable and is cached, even if empty.
    /// </summary>
    /// <param name="probeOnly">A flag indicating the caller is only probing; a missing ID returns empty without throwing (debug) or logging</param>
    /// <returns>The ID or an empty string</returns>
    public static string GetMachineID(bool probeOnly = false)
    {
        if (_machineID == null)
        {
            var value = ProbeIdFile(MACHINE_FILE, out var fileExists);
            if (string.IsNullOrWhiteSpace(value))
                value = GetInstallID(probeOnly: true);

            if (!fileExists && string.IsNullOrWhiteSpace(value))
                return ReportMissingIdValue(MACHINE_FILE, probeOnly);

            _machineID = value ?? "";
        }

        return _machineID;
    }

    /// <summary>
    /// Reads the first non-empty line of an ID file in the data folder, without creating anything
    /// </summary>
    /// <param name="filename">The name of the ID file to read</param>
    /// <param name="fileExists">Set to <c>true</c> if the file exists, even if it has no usable content</param>
    /// <returns>The trimmed ID, or <c>null</c> if the file has no content or cannot be read</returns>
    private static string? ProbeIdFile(string filename, out bool fileExists)
    {
        fileExists = false;
        try
        {
            var path = Path.Combine(GetDataFolder(AccessMode.ProbeOnly), filename);
            fileExists = File.Exists(path);
            if (fileExists)
                return File.ReadAllLines(path)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?.Trim();
        }
        catch
        {
            // Treat read errors as a missing file, so a later call retries
            fileExists = false;
        }

        return null;
    }

    /// <summary>
    /// Handles a request for an ID that could not be read from the data folder.
    /// In debug builds this throws, to reveal callers that read the ID before the data
    /// folder has been initialized; in release builds an empty string is returned.
    /// The empty value is never cached, so a later call can still pick up the ID.
    /// </summary>
    /// <param name="filename">The name of the ID file that had no value</param>
    /// <returns>An empty string in release builds; never returns in debug builds</returns>
    private static string ReportMissingIdValue(string filename, bool probeOnly)
    {
        if (probeOnly)
            return string.Empty;

#if DEBUG
        throw new InvalidOperationException($"The ID file \"{filename}\" in the data folder does not exist or is empty; the ID was requested before the data folder was initialized");
#else
        return string.Empty;
#endif
    }

    /// <summary>
    /// The machine name, lazy evaluated
    /// </summary>
    private static readonly Lazy<string> _machineName = new(MachineNameReader.GetMachineName);

    /// <summary>
    /// The machine name
    /// </summary>
    public static readonly string MachineName = _machineName.Value;

}