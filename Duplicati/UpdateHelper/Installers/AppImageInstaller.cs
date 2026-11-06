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
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs an AppImage by replacing the running image file
/// </summary>
internal sealed class AppImageInstaller(HelperLog log) : IInstaller
{
    private readonly string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly List<string> _cleanup = [];
    private string? _backup;

    public bool CanRollback => _backup != null && File.Exists(_backup);
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => _cleanup;

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsLinux())
            return "AppImage packages are only supported on Linux";
        if (string.IsNullOrWhiteSpace(job.AppImagePath))
            return "The running AppImage path is not known (APPIMAGE was not set)";
        if (!File.Exists(job.AppImagePath))
            return $"AppImage not found: {job.AppImagePath}";

        var dir = Path.GetDirectoryName(job.AppImagePath);
        if (string.IsNullOrEmpty(dir))
            return $"AppImage has no parent folder: {job.AppImagePath}";

        try
        {
            var probe = Path.Combine(dir, $".duplicati-update-probe-{_id}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return $"Cannot write to {dir}: {ex.Message}";
        }

        return null;
    }

    public void Install(UpdateJob job)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("AppImage packages are only supported on Linux");

        var target = job.AppImagePath!;
        var staged = $"{target}.update-{_id}";
        _backup = $"{target}.old-{_id}";

        log.Write($"Copying {job.PackagePath} to {staged}");
        File.Copy(job.PackagePath, staged, overwrite: true);
        File.SetUnixFileMode(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        log.Write($"Moving {target} to {_backup}");
        File.Move(target, _backup, overwrite: true);
        try
        {
            log.Write($"Moving {staged} to {target}");
            File.Move(staged, target, overwrite: true);
        }
        catch
        {
            File.Move(_backup, target, overwrite: true);
            _backup = null;
            throw;
        }

        _cleanup.Add(_backup);
    }

    public void Rollback(UpdateJob job)
    {
        if (_backup == null || !File.Exists(_backup))
            return;

        var target = job.AppImagePath!;
        var failed = $"{target}.failed-{_id}";
        log.Write($"Rolling back: moving {target} to {failed} and {_backup} back");
        if (File.Exists(target))
            File.Move(target, failed, overwrite: true);
        File.Move(_backup, target, overwrite: true);
        _cleanup.Remove(_backup);
        _cleanup.Add(failed);
        _backup = null;
    }
}
