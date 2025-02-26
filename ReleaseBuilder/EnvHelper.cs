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
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ReleaseBuilder;

/// <summary>
/// Static methods for working with environment variables
/// </summary>
public static class EnvHelper
{
    /// <summary>
    /// Reads the environment key, and expands environment variables inside.
    /// If no key is found, the default value is returned
    /// </summary>
    /// <param name="key">The key to use</param>
    /// <param name="defaultValue">The default value if the key is not set</param>
    /// <returns>The expanded string</returns>
    public static string ExpandEnv(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
            value = defaultValue ?? string.Empty;

        return ExpandEnv(value);
    }

    /// <summary>
    /// Reads the environment key, and expands environment variables inside.
    /// If no key is found, the default value is returned
    /// </summary>
    /// <param name="key">The key to use</param>
    /// <returns>The expanded string</returns>
    public static string ExpandEnv(string value)
        // Bash-style env expansion "${name}", done after normal env expansion
        => Regex.Replace(Environment.ExpandEnvironmentVariables(value), "\\${(?<name>[^}]+)}", m =>
            Environment.GetEnvironmentVariable(m.Groups["name"].Value) ?? string.Empty
        );

    /// <summary>
    /// Reads the environment key, and expands environment variables inside.
    /// If no key is found, the default value is returned
    /// </summary>
    /// <param name="key">The key to use</param>
    /// <param name="defaultValue">The default value if the key is not set</param>
    /// <returns>The value</returns>
    public static string GetEnvKey(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
            value = defaultValue ?? string.Empty;

        return value;
    }

    /// <summary>
    /// Extensions that are considered Windows executables
    /// </summary>
    private static readonly string[] WindowsExecutables = new[] { ".exe", ".cmd", ".ps1", ".bat" };

    /// <summary>
    /// Returns an executable path
    /// </summary>
    /// <param name="path">The path to expand</param>
    /// <returns>The executable path</returns>
    public static string[] GetExecutablePaths(string path)
        => string.IsNullOrWhiteSpace(path)
            ? []
            : OperatingSystem.IsWindows()
                ? WindowsExecutables.Select(x => Path.ChangeExtension(path, x)).ToArray()
                : [path];

    /// <summary>
    /// Returns a value if the path is executable
    /// </summary>
    /// <param name="path">The path to execute</param>
    /// <returns><c>true</c> if the path is executable; <c>false</c> otherwise</returns>
    public static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;

        if (OperatingSystem.IsWindows())
            return WindowsExecutables.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

        return File.GetUnixFileMode(path).HasFlag(UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Attempts to find the executable with the given name
    /// </summary>
    /// <param name="command">The command name</param>
    /// <param name="envkey">The env key for overrides</param>
    /// <param name="defaultValue">The default value</param>
    /// <returns>The command, or null</returns>
    public static string? FindCommand(string command, string? envkey, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(envkey))
        {
            var targets = GetExecutablePaths(ExpandEnv(envkey, ""));

            foreach (var target in targets)
            {
                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (!File.Exists(target))
                        throw new Exception($"Executable specified for {envkey} but not found: {target}");
                    if (!IsExecutable(target))
                        throw new Exception($"File specified for {envkey} found but is not executable: {target}");

                    return target;
                }
            }
        }

        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        return folders
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => GetExecutablePaths(Path.Combine(x, command)))
            .FirstOrDefault(IsExecutable)
                ?? defaultValue;
    }

    /// <summary>
    /// Copies the contents of <paramref name="sourceDir"/> into <paramref name="targetPath"/>.
    /// The <paramref name="sourceDir"/> must exist, and the contents are copied, not the folder itself.
    /// The <paramref name="targetPath"/> can exist, in which case the contents are not deleted, but overwritten (merged)
    /// </summary>
    /// <param name="sourceDir">The directory to copy</param>
    /// <param name="targetPath"></param>
    /// <param name="recursive"></param>
    /// <exception cref="Exception"></exception>
    public static void CopyDirectory(string sourceDir, string targetPath, bool recursive)
    {
        if (!Directory.Exists(sourceDir))
            throw new Exception($"Directory is missing: {sourceDir}");

        if (!Directory.Exists(targetPath))
            Directory.CreateDirectory(targetPath);

        foreach (var f in Directory.EnumerateFileSystemEntries(sourceDir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            if (File.Exists(f))
                File.Copy(f, Path.Combine(targetPath, Path.GetRelativePath(sourceDir, f)), true);
            else if (recursive && Directory.Exists(f))
            {
                var tg = Path.Combine(Path.Combine(targetPath, Path.GetRelativePath(sourceDir, f)));
                if (!Directory.Exists(tg))
                    Directory.CreateDirectory(tg);
            }
        }
    }

    /// <summary>
    /// Changes ownership of the path to the user and group
    /// </summary>
    /// <param name="path">The path to operate on</param>
    /// <param name="user">The user to change to</param>
    /// <param name="group">The group to change to</param>
    /// <param name="recursive">If the operation should be recursive</param>
    /// <returns>An awaitable task</returns>
    [UnsupportedOSPlatform("windows")]
    public static Task Chown(string path, string user, string group, bool recursive)
        // TODO: Requires sudo, and the Docker workaround does not work on MacOS
        => Task.CompletedTask;

    /// <summary>
    /// Changes ownership of the path to the user and group
    /// </summary>
    /// <param name="path">The path to operate on</param>
    /// <param name="user">The user to change to</param>
    /// <param name="group">The group to change to</param>
    /// <param name="recursive">If the operation should be recursive</param>
    /// <returns>An awaitable task</returns>
    [UnsupportedOSPlatform("windows")]
    private static async Task ChownWitDocker(string path, string user, string group, bool recursive)
    {
        // Get the numeric UID and GID for use in Docker
        var uid = int.Parse(await ProcessHelper.ExecuteWithOutput(new[] { "id", "-u", user }));
        var gid = int.Parse(
            OperatingSystem.IsMacOS()
                ? (await ProcessHelper.ExecuteWithOutput(["dscl", ".", "-read", $"/Groups/{group}", "PrimaryGroupID"])).Trim().Split(":", 2)[1].Trim()
                : (await ProcessHelper.ExecuteWithOutput(new[] { "getent", "group", group })).Trim().Split(":", 3)[2]
        );

        var baseFolder = Path.GetDirectoryName(path);
        var targetEntry = Path.GetFileName(path);

        // Use docker to set the ownership
        await ProcessHelper.Execute(["docker", "run", "--mount", $"type=bind,source={baseFolder},target=/opt/mount", "alpine:latest", "chown", recursive ? "-R" : "", $"{uid}:{gid}", Path.Combine("/opt/mount", targetEntry)]);
    }

    /// <summary>
    /// Returns the unix file mode pattern represented by the mode string
    /// </summary>
    /// <param name="modestr">The unix mode string, e.g. &quot;+x&quot;</param>
    /// <returns>The unix file mode</returns>
    public static UnixFileMode GetUnixFileMode(string modestr)
    {
        var current = UnixFileMode.None;

        var mmatch = Regex.Match(modestr, @"^((?<who>[augo]{0,3})(?<op>\+|\-)(?<mode>[rwx]{1,3}))$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        if (!mmatch.Success)
            throw new Exception($"Invalid mode string: {modestr}");

        var who = mmatch.Groups["who"].Value.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(who) || who.Contains('a'))
            who = "ugo";

        var op = mmatch.Groups["op"].Value;
        var mode = mmatch.Groups["mode"].Value.ToLowerInvariant();

        foreach (var m in mode)
            foreach (var w in who)
            {
                var p = $"{w}{m}" switch
                {
                    "ur" => UnixFileMode.UserRead,
                    "uw" => UnixFileMode.UserWrite,
                    "ux" => UnixFileMode.UserExecute,
                    "gr" => UnixFileMode.GroupRead,
                    "gw" => UnixFileMode.GroupWrite,
                    "gx" => UnixFileMode.GroupExecute,
                    "or" => UnixFileMode.OtherRead,
                    "ow" => UnixFileMode.OtherWrite,
                    "ox" => UnixFileMode.OtherExecute,
                    _ => throw new Exception("Unsupported bitflag combo")
                };

                current |= p;
            }

        return current;
    }

    /// <summary>
    /// Helper function to add unix filemode bits
    /// </summary>
    /// <param name="path">The path to operate on (must exist)</param>
    /// <param name="mode">The unix file mode</param>
    [UnsupportedOSPlatform("windows")]
    public static void AddFilemode(string path, UnixFileMode mode)
        => File.SetUnixFileMode(path, File.GetUnixFileMode(path) | mode);

    /// <summary>
    /// Helper function to remove unix filemode bits
    /// </summary>
    /// <param name="path">The path to operate on (must exist)</param>
    /// <param name="mode">The unix file mode</param>
    [UnsupportedOSPlatform("windows")]
    public static void RemoveFilemode(string path, UnixFileMode mode)
        => File.SetUnixFileMode(path, File.GetUnixFileMode(path) & ~mode);

    /// <summary>
    /// Helper function to set unix filemode
    /// </summary>
    /// <param name="path">The path to operate on (must exist)</param>
    /// <param name="modestr">The unix mode string, e.g. &quot;+x&quot;</param>
    [UnsupportedOSPlatform("windows")]
    public static void SetFilemode(string path, string modestr)
    {
        if (modestr.Contains("+"))
            AddFilemode(path, GetUnixFileMode(modestr));
        else
            RemoveFilemode(path, GetUnixFileMode(modestr));
    }
}
