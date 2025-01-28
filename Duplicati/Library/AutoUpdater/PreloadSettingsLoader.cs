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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Utility class for loading the preload settings
/// </summary>
public static class PreloadSettingsLoader
{
    /// <summary>
    /// The environment variable to specify the preload settings file
    /// </summary>
    private const string PreloadSettingsEnvVar = "DUPLICATI_PRELOAD_SETTINGS";
    /// <summary>
    /// The environment variable to enable debug output for preload settings
    /// </summary>
    private const string PreloadSettingsDebugEnvVar = "DUPLICATI_PRELOAD_SETTINGS_DEBUG";
    /// <summary>
    /// The marker for any executable
    /// </summary>
    private const string AnyExecutableMarker = "*";

    /// <summary>
    /// Cached value for toggling debug code
    /// </summary>
    private static readonly bool PreloadDebug = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PreloadSettingsDebugEnvVar));

    /// <summary>
    /// The folder name for the preload settings
    /// </summary>
    private const string FOLDER_NAME = "Duplicati";
    /// <summary>
    /// The file name for the preload settings
    /// </summary>
    private const string FILE_NAME = "preload.json";

    /// <summary>
    /// The path for preload settings specified with an environment variable
    /// </summary>
    private static readonly string PreloadSettingsFile = Environment.GetEnvironmentVariable(PreloadSettingsEnvVar) ?? "";

    /// <summary>
    /// Paths that are suited for portable installations
    /// </summary>
    private static readonly string[] PortablePreloadPaths = new string[] {
    
        // The path for preload settings with the install directory
        Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            FILE_NAME
        ),

        // The path for preload settings specified with an environment variable
        PreloadSettingsFile,
    }
    .Where(x => !string.IsNullOrEmpty(x))
    .Distinct()
    .ToArray();

    /// <summary>
    /// The preload paths to search for settings in.
    /// Each path is checked and applied to obtain the final settings.
    /// Later paths take precedence over earlier ones, so the env variable is most specific.
    /// These are statically loaded so the preload cannot change the settings after startup.
    /// </summary>
    private static readonly string[] PreloadPaths = new string[]
    {
        // The default path for preload settings
        OperatingSystem.IsMacOS()
            ? ""
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                FOLDER_NAME,
                FILE_NAME
        ),

        // MacOS reports CommonApplicationData as /usr/share, which is not writable
        // so we use /usr/local/share instead
        OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()
            ? Path.Combine("/usr/local/share", FOLDER_NAME, FILE_NAME)
            : "",

        // User-context path for preload settings,
        // the same default path as where other data is stored
        Path.Combine(DataFolderManager.DATAFOLDER, FILE_NAME),
    }
    .Concat(PortablePreloadPaths)
    .Where(x => !string.IsNullOrEmpty(x))
    .Distinct()
    .ToArray();

    /// <summary>
    /// Configures the preload settings for the given executable
    /// </summary>
    /// <param name="arguments">The source commandline arguments</param>
    /// <param name="executable">The executable to match</param>
    public static void ConfigurePreloadSettings(ref string[] arguments, PackageHelper.NamedExecutable executable)
        => ConfigurePreloadSettings(ref arguments, executable, out _);

    /// <summary>
    /// Configures the preload settings for the given executable
    /// </summary>
    /// <param name="arguments">The source commandline arguments</param>
    /// <param name="executable">The executable to match</param>
    /// <param name="dbsettings">The database settings</param>
    public static void ConfigurePreloadSettings(ref string[] arguments, PackageHelper.NamedExecutable executable, out Dictionary<string, string?> dbsettings)
    {
        var (env, args, db) = GetExecutableMergedSettings(executable, DataFolderManager.PORTABLE_MODE);

        dbsettings = db;
        ApplyEnvironmentVariables(env);
        ApplyCommandLineArguments(ref arguments, args);
    }

    /// <summary>
    /// Gets the argument name from the given argument
    /// </summary>
    /// <param name="arg">The argument to get the name from</param>
    /// <returns>The argument name</returns>
    private static string GetArgumentName(string arg)
        => arg.Split('=', 2)[0];


    /// <summary>
    /// Gets the merged settings for the given executable
    /// </summary>
    /// <param name="executable">The executable to get settings for</param>
    /// <param name="sourceargs">The source commandline arguments</param>
    /// <param name="portableMode">The portable mode flag</param>
    /// <returns>The merged settings</returns>
    private static (Dictionary<string, string> env, List<string> args, Dictionary<string, string?> db) GetExecutableMergedSettings(PackageHelper.NamedExecutable executable, bool portableMode)
    {
        // Collect settings in generic and specific dictionaries
        // The executable-specific settings take precedence over the generic ones,
        // but the loading order is used so that the most specific file is loaded last

        var env_generic = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var env_specific = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var args_generic = new List<string>();
        var args_specific = new List<string>();

        var db_generic = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var db_specific = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var exename = MapExecutableName(executable);

        void MergeDicts(Dictionary<string, string?> target, Dictionary<string, string?>? source)
        {
            if (source != null)
                foreach (var kvp in source)
                    target[kvp.Key] = kvp.Value;
        }

        var paths = PreloadPaths;
        if (portableMode)
        {
            if (PreloadDebug)
                Console.WriteLine("Portable mode detected, using portable preload paths only");
            paths = PortablePreloadPaths;
        }

        if (!string.IsNullOrWhiteSpace(PreloadSettingsFile) && PreloadSettingsFile.StartsWith("!"))
        {
            if (PreloadDebug)
                Console.WriteLine($"Preload settings file specified with environment variable and exclamation mark, ignoring others");
            paths = new[] { PreloadSettingsFile.Substring(1) };
        }

        foreach (var path in paths)
        {
            if (!Path.IsPathRooted(path))
            {
                if (PreloadDebug)
                    Console.WriteLine($"Preload settings path is not rooted, ignoring: {path}");
                continue;
            }

            if (!File.Exists(path))
            {
                if (PreloadDebug)
                    Console.WriteLine($"Preload settings file does not exist, ignoring: {path}");
                continue;
            }

            var settings = LoadSettings(path);

            if (settings == null)
                continue;

            if (settings.db != null)
            {
                if (settings.db.TryGetValue(AnyExecutableMarker, out var entry))
                    MergeDicts(db_generic, entry);

                if (exename != AnyExecutableMarker && settings.db.TryGetValue(exename, out entry))
                    MergeDicts(db_specific, entry);
            }

            if (settings.env != null)
            {
                if (settings.env.TryGetValue(AnyExecutableMarker, out var entry))
                    MergeDicts(env_generic, entry);

                if (exename != AnyExecutableMarker && settings.env.TryGetValue(exename, out entry))
                    MergeDicts(env_specific, entry);
            }

            if (settings.args != null)
            {
                if (settings.args.TryGetValue(AnyExecutableMarker, out var entry))
                    args_generic.AddRange(entry ?? []);

                if (exename != AnyExecutableMarker && settings.args.TryGetValue(exename, out entry))
                    args_specific.AddRange(entry ?? []);
            }
        }

        // Merge specific settings into generic ones
        foreach (var kvp in db_specific)
            db_generic[kvp.Key] = kvp.Value;
        foreach (var kvp in env_specific)
            env_generic[kvp.Key] = kvp.Value;

        args_generic.AddRange(args_specific);

        // Remove duplicates from the arguments, preserve order
        var mapped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var args = new List<string>();

        foreach (var value in args_generic)
        {
            var argname = GetArgumentName(value);
            if (mapped.TryGetValue(argname, out var index))
                args.RemoveAt(index);

            mapped[argname] = args.Count;
            args.Add(value);
        }

        var env = env_generic.ToDictionary(x => x.Key, x => x.Value ?? "");
        return (env, args, db_generic);
    }

    /// <summary>
    /// Applies loaded environment variables, but does not overwrite existing ones
    /// </summary>
    /// <param name="env">The environment variables to apply</param>
    private static void ApplyEnvironmentVariables(Dictionary<string, string> env)
    {
        var current = Environment.GetEnvironmentVariables();
        foreach (var kvp in env)
            if (!current.Contains(kvp.Key))
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value ?? "");
    }

    /// <summary>
    /// Applies loaded commandline arguments, but does not overwrite existing ones
    /// </summary>
    /// <param name="arguments">The source commandline arguments</param>
    /// <param name="args">The arguments to apply</param>
    private static void ApplyCommandLineArguments(ref string[] arguments, List<string> args)
    {
        arguments ??= [];
        var existing = new HashSet<string>(arguments.Select(GetArgumentName), StringComparer.OrdinalIgnoreCase);
        var result = arguments.ToList();

        foreach (var value in args)
            if (!existing.Contains(GetArgumentName(value)))
                result.Add(value);

        arguments = result.ToArray();
    }

    /// <summary>
    /// Loads the settings from the given path
    /// </summary>
    /// <param name="path">The path to load settings from</param>
    /// <returns>The loaded settings, or null if an error occurred</returns>
    private static PreloadSettingsRoot? LoadSettings(string path)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PreloadSettingsRoot>(File.ReadAllText(path));
            if (PreloadDebug)
            {
                if (result == null)
                {
                    if (PreloadDebug)
                        Console.WriteLine($"Loaded empty preload settings from {path}");
                    return null;
                }

                var jsData = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
                var unmatched_keys = jsData.EnumerateObject().Select(x => x.Name).Except(["db", "env", "args"]);
                if (PreloadDebug)
                {
                    if (unmatched_keys.Any())
                        Console.WriteLine($"Unexpected key(s) in preload settings: {string.Join(", ", unmatched_keys)}");

                    Console.WriteLine($"Loaded preload settings from {path}");
                }

                var allowedSources = Enum.GetValues<PackageHelper.NamedExecutable>()
                    .Select(MapExecutableName)
                    .Append(AnyExecutableMarker)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var unmatched_db = result.db?.Keys.Where(x => !allowedSources.Contains(x)) ?? [];
                var unmatched_env = result.env?.Keys.Where(x => !allowedSources.Contains(x)) ?? [];
                var unmatched_args = result.args?.Keys.Where(x => !allowedSources.Contains(x)) ?? [];

                if (PreloadDebug)
                {
                    if (unmatched_db.Any())
                        Console.WriteLine($"Found unknown executable name(s) in db preload settings: {string.Join(", ", unmatched_db)}");
                    if (unmatched_env.Any())
                        Console.WriteLine($"Found unknown executable name(s) in env preload settings: {string.Join(", ", unmatched_env)}");
                    if (unmatched_args.Any())
                        Console.WriteLine($"Found unknown executable name(s) in args preload settings: {string.Join(", ", unmatched_args)}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Logging is usually not set up at this point
            if (PreloadDebug)
                Console.WriteLine($"Failed to load preload settings from {path}: {ex}");

            return null;
        }
    }

    /// <summary>
    /// Maps the executable name to the preload settings key
    /// </summary>
    /// <param name="exe">The executable to map</param>
    /// <returns>The mapped name</returns>
    private static string MapExecutableName(PackageHelper.NamedExecutable exe)
        => exe switch
        {
            PackageHelper.NamedExecutable.TrayIcon => "tray",
            PackageHelper.NamedExecutable.CommandLine => "cli",
            PackageHelper.NamedExecutable.AutoUpdater => "autoupdater",
            PackageHelper.NamedExecutable.Server => "server",
            PackageHelper.NamedExecutable.WindowsService => "winservice",
            PackageHelper.NamedExecutable.BackendTool => "backendtool",
            PackageHelper.NamedExecutable.RecoveryTool => "recoverytool",
            PackageHelper.NamedExecutable.BackendTester => "backendtester",
            PackageHelper.NamedExecutable.SharpAESCrypt => "aescrypt",
            PackageHelper.NamedExecutable.Snapshots => "snapshots",
            PackageHelper.NamedExecutable.ServerUtil => "serverutil",
            PackageHelper.NamedExecutable.Service => "service",
            PackageHelper.NamedExecutable.Agent => "agent",
            _ => AnyExecutableMarker,
        };

    /// <summary>
    /// JSON root object for preload settings
    /// </summary>
    /// <param name="db">The database settings</param>
    /// <param name="env">The environment variables</param>
    /// <param name="args">The executable settings</param>
    private sealed record PreloadSettingsRoot(
        Dictionary<string, Dictionary<string, string?>>? db,
        Dictionary<string, Dictionary<string, string?>>? env,
        Dictionary<string, List<string>>? args
    );

}
