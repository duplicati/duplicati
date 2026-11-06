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
using System.Linq;
using System.Threading;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs a macOS disk image: mounts it, copies the application bundle next to the
/// installed one with ditto, and swaps the bundles by renaming. The previous bundle is kept
/// as <c>Name.app.old-&lt;id&gt;</c> until the new instance runs, so a failed start can roll back.
/// When the folder holding the bundle is not writable, the swap runs through one
/// administrator prompt.
/// </summary>
internal sealed class DmgInstaller(HelperLog log) : IInstaller
{
    private const string HdiutilPath = "/usr/bin/hdiutil";
    private const string DittoPath = "/usr/bin/ditto";
    private const string PackageTypeFile = "package_type_id.txt";

    private readonly string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly List<string> _cleanup = [];
    private string? _bundle;
    private string? _appName;
    private string? _oldBundle;
    private bool _swapped;
    private bool _elevated;

    public bool CanRollback => _swapped && _oldBundle != null && Directory.Exists(_oldBundle);
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => _cleanup;

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsMacOS())
            return "dmg packages are only supported on macOS";
        if (!File.Exists(HdiutilPath))
            return $"{HdiutilPath} was not found";
        if (!File.Exists(DittoPath))
            return $"{DittoPath} was not found";

        _bundle = FindBundleRoot(job.InstallDir);
        if (_bundle == null)
            return $"The install folder {job.InstallDir} is not inside an application bundle";

        var parent = Path.GetDirectoryName(_bundle);
        if (string.IsNullOrEmpty(parent))
            return $"The application bundle has no parent folder: {_bundle}";

        if (!CanWrite(parent))
        {
            if (!job.InteractiveSession)
                return $"Cannot write to {parent} and no interactive session is available to ask for administrator privileges";
            _elevated = true;
            log.Write($"{parent} is not writable; the swap will run with administrator privileges");
        }

        string? mount = null;
        try
        {
            mount = Mount(job.PackagePath);
            var app = FindAppInImage(mount);
            if (app == null)
                return "The disk image does not contain an application bundle";

            var typeFile = Path.Combine(app, "Contents", "MacOS", PackageTypeFile);
            if (!File.Exists(typeFile))
                return $"The application in the disk image has no {PackageTypeFile}";

            var id = File.ReadAllText(typeFile).Trim();
            if (!string.Equals(id, job.PackageTypeId, StringComparison.OrdinalIgnoreCase))
                return $"The package is for {id}, but this installation is {job.PackageTypeId}";

            _appName = Path.GetFileName(app);
        }
        catch (Exception ex)
        {
            return $"Cannot read the disk image: {ex.Message}";
        }
        finally
        {
            if (mount != null)
                Detach(mount);
        }

        return null;
    }

    public void Install(UpdateJob job)
    {
        var bundle = _bundle ?? throw new InvalidOperationException("Prepare was not called");
        var parent = Path.GetDirectoryName(bundle)!;
        var name = Path.GetFileName(bundle);
        var staged = Path.Combine(parent, $".{name}.update-{_id}");
        var old = Path.Combine(parent, $"{name}.old-{_id}");

        if (_elevated)
            InstallElevated(job, bundle, staged, old);
        else
            InstallAsUser(job, bundle, staged, old);

        _oldBundle = old;
        _swapped = true;
        _cleanup.Add(old);
    }

    /// <summary>
    /// Swaps the bundle with administrator privileges. The image is copied, checked and mounted by root,
    /// so the user cannot replace the image or the mount between the check and the copy into the target folder.
    /// </summary>
    private void InstallElevated(UpdateJob job, string bundle, string staged, string old)
    {
        var appName = _appName ?? throw new InvalidOperationException("Prepare was not called");
        // The mount point lives in the root-only folder of the script
        const string mount = "\"$WORKDIR/mnt\"";
        var app = "\"$WORKDIR/mnt/\"" + Q(appName);

        // One prompt covers the whole swap; the copy ends up owned by the user, like a drag install,
        // so the new instance can delete the old bundle later
        var uid = ProcessInfo.UnixUserId();
        var command = string.Join(" && ", [
            $"/bin/mkdir {mount}",
            $"{Q(HdiutilPath)} attach {ElevatedPackage.PackageVariable} -nobrowse -readonly -noautoopen -quiet -mountpoint {mount}",
            $"{Q(DittoPath)} {app} {Q(staged)}",
            $"/usr/sbin/chown -R {uid} {Q(staged)}",
            $"/bin/mv {Q(bundle)} {Q(old)}",
            $"{{ /bin/mv {Q(staged)} {Q(bundle)} || {{ /bin/mv {Q(old)} {Q(bundle)}; exit 1; }}; }}",
        ]);
        var cleanup = $"{Q(HdiutilPath)} detach {mount} -force -quiet >/dev/null 2>&1; /bin/rmdir {mount} 2>/dev/null";

        log.Write($"Replacing {bundle} with administrator privileges");
        var r = ElevatedPackage.Run(log, job, "dmg", command, cleanup, null, true, TimeSpan.FromMinutes(10));
        if (!r.Success)
        {
            TryDelete(staged);
            throw new InvalidOperationException($"The elevated install failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
        }
    }

    /// <summary>
    /// Swaps the bundle as the current user
    /// </summary>
    private void InstallAsUser(UpdateJob job, string bundle, string staged, string old)
    {
        var mount = Mount(job.PackagePath);
        try
        {
            var app = FindAppInImage(mount) ?? throw new InvalidOperationException("The disk image does not contain an application bundle");

            log.Write($"Copying {app} to {staged}");
            var copy = Shell.Run(log, DittoPath, [app, staged], null, TimeSpan.FromMinutes(10));
            if (!copy.Success)
            {
                TryDelete(staged);
                throw new InvalidOperationException($"ditto failed with exit code {copy.ExitCode}: {copy.StdErr.Trim()}");
            }

            log.Write($"Moving {bundle} to {old}");
            Directory.Move(bundle, old);
            try
            {
                log.Write($"Moving {staged} to {bundle}");
                Directory.Move(staged, bundle);
            }
            catch
            {
                log.Write("Restoring the previous bundle");
                Directory.Move(old, bundle);
                TryDelete(staged);
                throw;
            }
        }
        finally
        {
            Detach(mount);
        }
    }

    public void Rollback(UpdateJob job)
    {
        if (!_swapped || _bundle == null || _oldBundle == null || !Directory.Exists(_oldBundle))
            return;

        var failed = $"{_bundle}.failed-{_id}";
        log.Write($"Rolling back: moving {_bundle} to {failed} and {_oldBundle} back");

        if (_elevated)
        {
            var script = string.Join(" && ", [
                $"if [ -e {Q(_bundle)} ]; then /bin/mv {Q(_bundle)} {Q(failed)}; fi",
                $"/bin/mv {Q(_oldBundle)} {Q(_bundle)}",
            ]);
            var r = Privileges.RunAsRoot(log, "/bin/sh", ["-c", script], null, true, TimeSpan.FromMinutes(5));
            if (!r.Success)
                throw new InvalidOperationException($"The elevated rollback failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
        }
        else
        {
            if (Directory.Exists(_bundle))
                Directory.Move(_bundle, failed);
            Directory.Move(_oldBundle, _bundle);
        }

        _swapped = false;
        _cleanup.Remove(_oldBundle);
        _cleanup.Add(failed);
    }

    /// <summary>
    /// Finds the application bundle that contains the install folder, by walking up to the first <c>.app</c> folder with an Info.plist
    /// </summary>
    /// <param name="installDir">The install folder, normally <c>Name.app/Contents/MacOS</c></param>
    /// <returns>The bundle path, or null if the folder is not inside a bundle</returns>
    internal static string? FindBundleRoot(string installDir)
    {
        var dir = ZipInstaller.NormalizeDir(installDir);
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(dir, "Contents", "Info.plist")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// Finds the application bundle at the root of a mounted image
    /// </summary>
    internal static string? FindAppInImage(string mount)
        => Directory.EnumerateDirectories(mount, "*.app")
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "Contents", "Info.plist")));

    private string Mount(string dmg)
    {
        var mount = Path.Combine(Path.GetTempPath(), $"duplicati-update-{_id}");
        Directory.CreateDirectory(mount);

        var r = Shell.Run(log, HdiutilPath, ["attach", dmg, "-nobrowse", "-readonly", "-noautoopen", "-quiet", "-mountpoint", mount], null, TimeSpan.FromMinutes(5));
        if (!r.Success)
        {
            try { Directory.Delete(mount, false); } catch { }
            throw new InvalidOperationException($"hdiutil attach failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
        }

        return mount;
    }

    private void Detach(string mount)
    {
        var r = Shell.Run(log, HdiutilPath, ["detach", mount, "-quiet"], null, TimeSpan.FromMinutes(2));
        if (!r.Success)
        {
            Thread.Sleep(2000);
            r = Shell.Run(log, HdiutilPath, ["detach", mount, "-quiet", "-force"], null, TimeSpan.FromMinutes(2));
            if (!r.Success)
                log.Write($"Could not detach {mount}: {r.StdErr.Trim()}");
        }

        try { Directory.Delete(mount, false); } catch { }
    }

    private bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".duplicati-update-probe-{_id}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            log.Write($"Cannot write to {dir}: {ex.Message}");
            return false;
        }
    }

    private void TryDelete(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        try { Directory.Delete(dir, true); }
        catch (Exception ex) { log.Write($"Could not delete {dir}: {ex.Message}"); }
    }

    private static string Q(string value)
        => Privileges.ShellQuote(value);
}
