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


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs a zip package by extracting it next to the install folder and swapping the folders
/// </summary>
internal sealed class ZipInstaller(HelperLog log) : IInstaller
{
    /// <summary>
    /// The name of the file that identifies the package type, must match the running one
    /// </summary>
    private const string PackageTypeFile = "package_type_id.txt";
    /// <summary>
    /// The folder below a portable install that holds the data; never touched
    /// </summary>
    private const string PortableDataFolder = "data";

    private readonly string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly List<string> _cleanup = [];
    private string? _oldDir;
    private string? _stagingDir;
    private bool _swapped;

    public bool CanRollback => _swapped && _oldDir != null && Directory.Exists(_oldDir);
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => _cleanup;

    public string? Prepare(UpdateJob job)
    {
        var installDir = NormalizeDir(job.InstallDir);
        if (!Directory.Exists(installDir))
            return $"Install folder not found: {installDir}";

        var parent = Path.GetDirectoryName(installDir);
        if (string.IsNullOrEmpty(parent))
            return $"Install folder has no parent: {installDir}";

        try
        {
            var probe = Path.Combine(parent, $".duplicati-update-probe-{_id}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return $"Cannot write to {parent}: {ex.Message}";
        }

        try
        {
            using var zip = ZipFile.OpenRead(job.PackagePath);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), PackageTypeFile, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return $"The package does not contain {PackageTypeFile}";

            using var sr = new StreamReader(entry.Open());
            var id = sr.ReadToEnd().Trim();
            if (!string.Equals(id, job.PackageTypeId, StringComparison.OrdinalIgnoreCase))
                return $"The package is for {id}, but this installation is {job.PackageTypeId}";
        }
        catch (Exception ex)
        {
            return $"Cannot read the package: {ex.Message}";
        }

        return null;
    }

    public void Install(UpdateJob job)
    {
        var installDir = NormalizeDir(job.InstallDir);
        var parent = Path.GetDirectoryName(installDir)!;
        var name = Path.GetFileName(installDir);

        _stagingDir = Path.Combine(parent, $".{name}.update-{_id}");
        log.Write($"Extracting {job.PackagePath} to {_stagingDir}");
        ZipFile.ExtractToDirectory(job.PackagePath, _stagingDir, overwriteFiles: true);

        var root = FindPackageRoot(_stagingDir);
        log.Write($"Package root is {root}");

        if (job.PortableMode)
        {
            log.Write("Portable mode, copying files over the install folder");
            CopyOver(root, installDir, skipFolder: PortableDataFolder);
        }
        else
        {
            _oldDir = Path.Combine(parent, $"{name}.old-{_id}");
            log.Write($"Moving {installDir} to {_oldDir}");
            try
            {
                Directory.Move(installDir, _oldDir);
            }
            catch (Exception ex) when (OperatingSystem.IsWindows())
            {
                log.Write($"Move failed ({ex.Message}), falling back to copying files over the install folder");
                _oldDir = null;
                CopyOver(root, installDir, skipFolder: null);
                TryDelete(_stagingDir);
                return;
            }

            try
            {
                log.Write($"Moving {root} to {installDir}");
                Directory.Move(root, installDir);
                _swapped = true;
            }
            catch
            {
                log.Write("Restoring the previous install folder");
                Directory.Move(_oldDir, installDir);
                _oldDir = null;
                throw;
            }
        }

        TryDelete(_stagingDir);

        // The old folder stays until the new instance runs, so a failed start can roll back; the new instance deletes it
        if (_oldDir != null)
            _cleanup.Add(_oldDir);
    }

    public void Rollback(UpdateJob job)
    {
        if (!_swapped || _oldDir == null || !Directory.Exists(_oldDir))
            return;

        var installDir = NormalizeDir(job.InstallDir);
        var failedDir = $"{installDir}.failed-{_id}";
        log.Write($"Rolling back: moving {installDir} to {failedDir} and {_oldDir} back");
        if (Directory.Exists(installDir))
            Directory.Move(installDir, failedDir);
        Directory.Move(_oldDir, installDir);
        _swapped = false;
        _cleanup.Remove(_oldDir);
        _cleanup.Add(failedDir);
    }

    /// <summary>
    /// Finds the folder inside the extracted package that holds the files; zips have one top-level folder
    /// </summary>
    internal static string FindPackageRoot(string stagingDir)
    {
        if (File.Exists(Path.Combine(stagingDir, PackageTypeFile)))
            return stagingDir;

        var dirs = Directory.GetDirectories(stagingDir);
        var files = Directory.GetFiles(stagingDir);
        if (dirs.Length == 1 && files.Length == 0 && File.Exists(Path.Combine(dirs[0], PackageTypeFile)))
            return dirs[0];

        throw new InvalidOperationException($"Could not locate {PackageTypeFile} in the extracted package");
    }

    /// <summary>
    /// Copies all files from the source over the target, renaming files that cannot be overwritten
    /// </summary>
    private void CopyOver(string source, string target, string? skipFolder)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            if (skipFolder != null && IsInFolder(rel, skipFolder))
                continue;
            Directory.CreateDirectory(Path.Combine(target, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (skipFolder != null && IsInFolder(rel, skipFolder))
                continue;

            var dest = Path.Combine(target, rel);
            try
            {
                File.Copy(file, dest, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A loaded executable or DLL cannot be overwritten on Windows, but it can be renamed
                var aside = $"{dest}.old-{_id}";
                log.Write($"Cannot overwrite {dest} ({ex.Message}), moving it aside to {aside}");
                File.Move(dest, aside, overwrite: true);
                File.Copy(file, dest, overwrite: true);
                _cleanup.Add(aside);
            }

            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(dest, File.GetUnixFileMode(file)); }
                catch (Exception ex) { log.Write($"Failed to set permissions on {dest}: {ex.Message}"); }
            }
        }
    }

    private static bool IsInFolder(string relativePath, string folder)
    {
        var first = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.Equals(first, folder, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryDelete(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return true;
        try
        {
            Directory.Delete(dir, true);
            return true;
        }
        catch (Exception ex)
        {
            log.Write($"Could not delete {dir}: {ex.Message}");
            return false;
        }
    }

    internal static string NormalizeDir(string dir)
        => Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
