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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Duplicati.Proprietary.DiskImage;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Manages a persistent PowerShell session for executing commands.
    /// This avoids the overhead of starting a new PowerShell process for each command.
    /// </summary>
    internal sealed class PowerShellSession : IDisposable
    {
        private Process? _process;
        private readonly object _lock = new();
        private bool _disposed;
        private readonly StringBuilder _outputBuffer = new();
        private readonly StringBuilder _errorBuffer = new();
        private readonly ManualResetEventSlim _outputAvailable = new(false);

        /// <summary>
        /// Gets a value indicating whether the PowerShell session is running.
        /// </summary>
        /// <value><c>true</c> if the session is running; otherwise, <c>false</c>.</value>
        public bool IsRunning => _process != null && !_process.HasExited;

        /// <summary>
        /// Ensures the PowerShell session is started.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the session has been disposed.</exception>
        public void EnsureStarted()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(PowerShellSession));
                }

                if (_process != null && !_process.HasExited)
                {
                    return;
                }

                StartProcess();
            }
        }

        /// <summary>
        /// Starts the PowerShell process with elevated privileges.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the PowerShell process cannot be started.</exception>
        private void StartProcess()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"& { while ($true) { $cmd = Read-Host; if ($cmd -eq 'EXIT_SESSION') { break }; try { $output = Invoke-Expression $cmd 2>&1; $output | Out-String -Stream; Write-Output \"___ENDOFCOMMAND___\" } catch { $_.Exception.Message; Write-Output \"___ENDOFCOMMAND___\" } } }\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas" // Request elevation
            };

            _process = Process.Start(startInfo);
            if (_process == null)
            {
                throw new InvalidOperationException("Failed to start PowerShell process");
            }

            // Clear any startup output
            _outputBuffer.Clear();
            _errorBuffer.Clear();
            _outputAvailable.Reset();

            // Attach event handlers for async reading
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// Handles the output data received from the PowerShell process.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                lock (_outputBuffer)
                {
                    _outputBuffer.AppendLine(e.Data);
                    if (e.Data.Contains("___ENDOFCOMMAND___"))
                    {
                        _outputAvailable.Set();
                    }
                }
            }
        }

        /// <summary>
        /// Handles the error data received from the PowerShell process.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                lock (_errorBuffer)
                {
                    _errorBuffer.AppendLine(e.Data);
                }
            }
        }

        /// <summary>
        /// Executes a PowerShell script and returns the output.
        /// </summary>
        /// <param name="script">The PowerShell script to execute.</param>
        /// <returns>The output from PowerShell.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the PowerShell process is not running.</exception>
        /// <exception cref="TimeoutException">Thrown if the command times out.</exception>
        public string ExecuteScript(string script)
        {
            lock (_lock)
            {
                EnsureStarted();

                if (_process == null || _process.HasExited)
                {
                    throw new InvalidOperationException("PowerShell process is not running");
                }

                // Clear previous output and reset the event
                lock (_outputBuffer)
                {
                    _outputBuffer.Clear();
                }
                lock (_errorBuffer)
                {
                    _errorBuffer.Clear();
                }
                _outputAvailable.Reset();

                // Write the script to stdin using base64 encoding to handle special characters
                var encodedScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
                var command = $"[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedScript}')) | Invoke-Expression";

                _process.StandardInput.WriteLine(command);

                // Wait for output with a timeout
                var timeout = TimeSpan.FromSeconds(30);
                if (!_outputAvailable.Wait(timeout))
                {
                    throw new TimeoutException($"PowerShell command timed out after {timeout.TotalSeconds} seconds.\nScript:\n{script}");
                }

                // Give a bit more time for all output to arrive
                Thread.Sleep(50);

                lock (_outputBuffer)
                {
                    var rawOutput = _outputBuffer.ToString();
                    _outputBuffer.Clear();

                    // Remove the end marker and any lines that contain our base64 command
                    var lines = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var filteredLines = lines
                        .Where(line => !line.Contains("___ENDOFCOMMAND___"))
                        .Where(line => !line.Contains("FromBase64String"))
                        .Where(line => !line.Contains(encodedScript.Substring(0, Math.Min(20, encodedScript.Length))))
                        .ToList();

                    var output = string.Join(Environment.NewLine, filteredLines).Trim();

                    lock (_errorBuffer)
                    {
                        var error = _errorBuffer.ToString().Trim();
                        _errorBuffer.Clear();

                        if (!string.IsNullOrEmpty(error))
                        {
                            throw new InvalidOperationException($"PowerShell error: {error}\nScript:\n{script}");
                        }
                    }

                    return output;
                }
            }
        }

        /// <summary>
        /// Disposes the PowerShell session.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_process != null)
                {
                    // Unregister event handlers
                    _process.OutputDataReceived -= OnOutputDataReceived;
                    _process.ErrorDataReceived -= OnErrorDataReceived;

                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.StandardInput.WriteLine("EXIT_SESSION");
                            if (!_process.WaitForExit(5000))
                            {
                                _process.Kill();
                            }
                        }
                        catch
                        {
                            // Ignore errors during cleanup
                            try { _process.Kill(); } catch { }
                        }
                    }

                    _process.Dispose();
                    _process = null;
                }

                _outputAvailable.Dispose();
            }
        }
    }

    /// <summary>
    /// Windows implementation of <see cref="IDiskImageHelper"/> using PowerShell and VHD files.
    /// </summary>
    internal class WindowsDiskImageHelper : IDiskImageHelper
    {
        // Static PowerShell session that persists across method calls
        private static PowerShellSession? _session;
        private static readonly object _sessionLock = new();

        /// <summary>
        /// Gets the shared PowerShell session, creating it if necessary.
        /// </summary>
        /// <returns>The shared <see cref="PowerShellSession"/> instance.</returns>
        private static PowerShellSession GetSession()
        {
            lock (_sessionLock)
            {
                if (_session == null)
                {
                    _session = new PowerShellSession();
                }
                return _session;
            }
        }

        /// <summary>
        /// Runs a PowerShell script using the persistent session.
        /// </summary>
        /// <param name="script">The PowerShell script to execute.</param>
        /// <returns>The output from PowerShell.</returns>
        private static string RunPowerShell(string script)
        {
            var session = GetSession();
            return session.ExecuteScript(script);
        }

        /// <summary>
        /// Extracts the disk number from a disk identifier string (e.g., "\\.\PhysicalDrive2" -> 2).
        /// </summary>
        /// <param name="diskIdentifier">The disk identifier string.</param>
        /// <returns>The disk number.</returns>
        private static int ParseDiskNumber(string diskIdentifier)
        {
            const string prefix = @"\\.\PhysicalDrive";
            if (diskIdentifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(diskIdentifier.Substring(prefix.Length), out int num))
                return num;

            throw new ArgumentException($"Invalid disk identifier: {diskIdentifier}. Expected format: \\\\.\\PhysicalDriveN", nameof(diskIdentifier));
        }

        /// <summary>
        /// Gets the disk number for a VHD image file.
        /// </summary>
        /// <param name="imagePath">The path to the VHD file.</param>
        /// <returns>The disk number, or -1 if not found.</returns>
        private static int GetDiskNumber(string imagePath)
        {
            try
            {
                var script = $@"
                    $image = Get-DiskImage -ImagePath '{imagePath}'
                    if ($image -and $image.Attached) {{
                        $image | Get-Disk | Select-Object -ExpandProperty Number
                    }}
                ";
                var result = RunPowerShell(script)?.Trim();

                if (!string.IsNullOrEmpty(result) && int.TryParse(result, out int diskNumber))
                    return diskNumber;
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: PowerShell GetDiskNumber failed: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Gets the drive letters (mount points) for all partitions on a disk.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>An array of mount point paths (e.g., "E:\").</returns>
        private static string[] GetMountPoints(int diskNumber)
        {
            var script = $@"
                Get-Partition -DiskNumber {diskNumber} |
                    Where-Object {{ $_.DriveLetter -and $_.DriveLetter -ne [char]0 }} |
                    ForEach-Object {{ $_.DriveLetter }}
            ";
            var result = RunPowerShell(script)?.Trim();

            if (string.IsNullOrEmpty(result))
                return [];

            return result
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length == 1 && char.IsLetter(l[0]))
                .Select(l => $"{char.ToUpperInvariant(l[0])}:\\")
                .ToArray();
        }

        /// <inheritdoc />
        public string CreateDisk(string imagePath, long sizeB)
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Delete existing VHD if it exists
            if (File.Exists(imagePath))
                try
                {
                    CleanupDisk(imagePath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }

            // Create the VHD using PowerShell
            var script = $@"New-VHD -Path '{imagePath}' -SizeBytes {sizeB} -Fixed | Out-Null; Mount-DiskImage -ImagePath '{imagePath}' | Out-Null";
            RunPowerShell(script);

            // Wait for the disk to be attached and get the disk number
            int diskNumber = WaitForDiskAttachment(imagePath, TimeSpan.FromSeconds(5));
            if (diskNumber < 0)
                throw new InvalidOperationException($"Failed to get disk number for VHD: {imagePath}");

            return $@"\\.\PhysicalDrive{diskNumber}";
        }

        /// <summary>
        /// Waits for a VHD to be attached and returns its disk number.
        /// </summary>
        /// <param name="imagePath">The path to the VHD file.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The disk number, or -1 if not found within the timeout.</returns>
        private static int WaitForDiskAttachment(string imagePath, TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                var diskNumber = GetDiskNumber(imagePath);
                if (diskNumber >= 0)
                    return diskNumber;
                Thread.Sleep(100);
            }
            return -1;
        }

        /// <inheritdoc />
        public string[] InitializeDisk(string diskIdentifier, PartitionTableType tableType, (FileSystemType, long)[] partitions)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);

            if (tableType == PartitionTableType.Unknown)
                return []; // No partition table, so nothing to initialize

            // Initialize the disk with the specified partition style
            var script = $@"
                $disk = Get-Disk -Number {diskNumber}
                if ($disk.PartitionStyle -ne 'RAW') {{
                    Clear-Disk -Number {diskNumber} -RemoveData -Confirm:$false
                }}
                Initialize-Disk -Number {diskNumber} -PartitionStyle {tableType.ToString().ToUpperInvariant()} -PassThru | Out-Null
            ";
            RunPowerShell(script);

            // Wait for the initialization to complete
            WaitForDiskInitialization(diskNumber, TimeSpan.FromSeconds(5));

            if (partitions.Length == 0)
                return [];

            // Create and format each partition
            foreach (var (fsType, sizeB) in partitions)
            {
                var fsTypeStr = fsType.ToString().ToUpperInvariant();
                var sizeParam = sizeB > 0 ? $"-Size {sizeB}" : "-UseMaximumSize";

                script = $@"
                    $partition = New-Partition -DiskNumber {diskNumber} {sizeParam} -AssignDriveLetter
                    # Wait for the partition to be ready
                    $timeout = (Get-Date).AddSeconds(30)
                    while ((Get-Date) -lt $timeout) {{
                        $vol = Get-Partition -DiskNumber {diskNumber} | Where-Object {{ $_.PartitionNumber -eq $partition.PartitionNumber }} | Get-Volume
                        if ($vol) {{ break }}
                        Start-Sleep -Milliseconds 100
                    }}
                    # Format the volume
                    Format-Volume -Partition $partition -FileSystem {fsTypeStr} -NewFileSystemLabel 'TestVol' -Confirm:$false | Out-Null
                ";
                RunPowerShell(script);
            }

            return GetMountPoints(diskNumber);
        }

        /// <summary>
        /// Waits for a disk to be initialized.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <exception cref="TimeoutException">Thrown if initialization times out.</exception>
        private static void WaitForDiskInitialization(int diskNumber, TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                var script = $"(Get-Disk -Number {diskNumber}).PartitionStyle";
                var result = RunPowerShell(script)?.Trim();
                if (result == "GPT" || result == "MBR")
                    return;
                Thread.Sleep(100);
            }
            throw new TimeoutException($"Disk {diskNumber} initialization timed out");
        }

        /// <inheritdoc />
        public string[] Mount(string diskIdentifier, string? baseMountPath = null, bool readOnly = false)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);

            // Check if already mounted
            try
            {
                var mountPoints = GetMountPoints(diskNumber);
                if (mountPoints.Length > 0)
                    return mountPoints;
            }
            catch
            {
                // Ignore errors and try mounting
            }

            // Bring the disk online and assign drive letters
            var script = $@"
                Set-Disk -Number {diskNumber} -IsOffline $false
                Update-Disk -Number {diskNumber}
                Update-HostStorageCache
                Get-Partition -DiskNumber {diskNumber} | Where-Object {{ $_.Type -ne 'Reserved' -and $_.Type -ne 'System' -and -not $_.DriveLetter }} | ForEach-Object {{
                    $_ | Add-PartitionAccessPath -AssignDriveLetter
                }}
            ";
            RunPowerShell(script);

            return WaitForMountPoints(diskNumber);
        }

        /// <summary>
        /// Waits for mount points to be fully established after a mount operation.
        /// Uses exponential backoff starting from a short delay to minimize wait time
        /// in the common case while still handling slow mounts.
        /// </summary>
        /// <param name="diskNumber">The disk number to check mount points for.</param>
        /// <returns>An array of mount point paths.</returns>
        private static string[] WaitForMountPoints(int diskNumber)
        {
            const int maxRetries = 10;
            const int initialDelayMs = 100;
            const int maxDelayMs = 1000;

            var delayMs = initialDelayMs;
            for (int i = 0; i < maxRetries; i++)
            {
                var mountPoints = GetMountPoints(diskNumber);

                if (mountPoints.Length > 0)
                {
                    // Check if all mount points have filesystem entries (not empty)
                    var allNonEmpty = mountPoints.All(mp =>
                    {
                        try { return Directory.GetFileSystemEntries(mp).Length > 0; }
                        catch { return false; }
                    });

                    if (allNonEmpty || i == maxRetries - 1)
                        return mountPoints;
                }

                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, maxDelayMs);
            }

            // Final attempt - return whatever we have
            return GetMountPoints(diskNumber);
        }

        /// <inheritdoc />
        public void Unmount(string diskIdentifier)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);

            // Remove drive letters from all partitions
            var script = $@"
                Get-Partition -DiskNumber {diskNumber} |
                    Where-Object {{ $_.DriveLetter -and $_.DriveLetter -ne [char]0 }} |
                    ForEach-Object {{
                        $letter = $_.DriveLetter
                        Remove-PartitionAccessPath -DiskNumber {diskNumber} -PartitionNumber $_.PartitionNumber -AccessPath ""$($letter):\""
                    }}
            ";
            RunPowerShell(script);

            // Pull the disk offline to ensure it's not in use
            script = $@"
                Set-Disk -Number {diskNumber} -IsOffline $true
                Set-Disk -Number {diskNumber} -IsReadOnly $false
            ";
            RunPowerShell(script);
        }

        /// <inheritdoc />
        public void FlushDisk(string diskIdentifier)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);
            try
            {
                // Flush all volumes on the disk
                var script = $@"
                    Get-Partition -DiskNumber {diskNumber} |
                        Where-Object {{ $_.DriveLetter -and $_.DriveLetter -ne [char]0 }} |
                        ForEach-Object {{
                            Write-VolumeCache -DriveLetter $_.DriveLetter
                        }}
                ";
                RunPowerShell(script);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to flush volume cache: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void CleanupDisk(string imagePath, string? diskIdentifier = null)
        {
            try
            {
                var script = $@"
                    $image = Get-DiskImage -ImagePath '{imagePath}'
                    if ($image -and $image.Attached) {{
                        Dismount-DiskImage -ImagePath '{imagePath}'
                    }}
                ";
                RunPowerShell(script);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to detach disk image: {ex.Message}");
            }

            try
            {
                if (File.Exists(imagePath))
                    File.Delete(imagePath);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to delete disk image file: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public bool HasRequiredPrivileges()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public PartitionTableGeometry GetPartitionTable(string diskIdentifier)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);

            var script = $@"
                $disk = Get-Disk -Number {diskNumber}
                Write-Output ""PartitionStyle:$($disk.PartitionStyle)""
                Write-Output ""Size:$($disk.Size)""
                Write-Output ""LogicalSectorSize:$($disk.LogicalSectorSize)""
            ";
            var output = RunPowerShell(script);

            PartitionTableType tableType = PartitionTableType.Unknown;
            long size = -1;
            int sectorSize = -1;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split([':'], 2);
                if (parts.Length != 2) continue;

                switch (parts[0].Trim())
                {
                    case "PartitionStyle":
                        tableType = parts[1].Trim() switch
                        {
                            "GPT" => PartitionTableType.GPT,
                            "MBR" => PartitionTableType.MBR,
                            _ => PartitionTableType.Unknown
                        };
                        break;
                    case "Size":
                        if (long.TryParse(parts[1].Trim(), out long parsedSize))
                            size = parsedSize;
                        break;
                    case "LogicalSectorSize":
                        if (int.TryParse(parts[1].Trim(), out int parsedSectorSize))
                            sectorSize = parsedSectorSize;
                        break;
                }
            }

            if (size == -1 || sectorSize == -1)
                throw new InvalidOperationException($"Failed to retrieve partition table information for disk {diskIdentifier}");

            return new PartitionTableGeometry
            {
                Type = tableType,
                Size = size,
                SectorSize = sectorSize
            };
        }

        /// <inheritdoc />
        public PartitionGeometry[] GetPartitions(string diskIdentifier)
        {
            var diskNumber = ParseDiskNumber(diskIdentifier);

            var script = $@"
                Get-Partition -DiskNumber {diskNumber} |
                    Where-Object {{ $_.Type -ne 'Reserved' -and $_.Type -ne 'System' }} |
                    ForEach-Object {{
                        $vol = $_ | Get-Volume
                        Write-Output ""---PARTITION---""
                        Write-Output ""Number:$($_.PartitionNumber)""
                        Write-Output ""Type:$($_.Type)""
                        Write-Output ""Offset:$($_.Offset)""
                        Write-Output ""Size:$($_.Size)""
                        Write-Output ""GptType:$($_.GptType)""
                        Write-Output ""Guid:$($_.Guid)""
                        if ($vol) {{
                            Write-Output ""FileSystem:$($vol.FileSystem)""
                            Write-Output ""FileSystemLabel:$($vol.FileSystemLabel)""
                        }}
                    }}
            ";
            var output = RunPowerShell(script);

            var partitions = new List<PartitionGeometry>();
            PartitionGeometry? current = null;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Trim() == "---PARTITION---")
                {
                    if (current != null)
                        partitions.Add(current);
                    current = new PartitionGeometry
                    {
                        Number = -1,
                        Type = PartitionType.Unknown,
                        StartOffset = -1,
                        Size = -1,
                        Name = null,
                        FilesystemType = FileSystemType.Unknown,
                        VolumeGuid = null,
                        TableType = PartitionTableType.Unknown
                    };
                    continue;
                }

                if (current == null) continue;

                var parts = line.Split([':'], 2);
                if (parts.Length != 2) continue;

                switch (parts[0].Trim())
                {
                    case "Number":
                        if (int.TryParse(parts[1].Trim(), out int num))
                            current.Number = num;
                        break;
                    case "Type":
                        current.Type = parts[1].Trim() switch
                        {
                            "Basic" => PartitionType.Primary,
                            "IFS" => PartitionType.Primary,
                            _ => PartitionType.Unknown
                        };
                        break;
                    case "Offset":
                        if (long.TryParse(parts[1].Trim(), out long offset))
                            current.StartOffset = offset;
                        break;
                    case "Size":
                        if (long.TryParse(parts[1].Trim(), out long size))
                            current.Size = size;
                        break;
                    case "Guid":
                        if (Guid.TryParse(parts[1].Trim(), out Guid guid))
                            current.VolumeGuid = guid;
                        break;
                    case "FileSystem":
                        current.FilesystemType = parts[1].Trim() switch
                        {
                            "NTFS" => FileSystemType.NTFS,
                            "FAT32" => FileSystemType.FAT32,
                            "exFAT" => FileSystemType.ExFAT,
                            "FAT" => FileSystemType.FAT32,
                            "ReFS" => FileSystemType.ReFS,
                            _ => FileSystemType.Unknown
                        };
                        break;
                    case "FileSystemLabel":
                        current.Name = parts[1].Trim();
                        break;
                }
            }

            if (current != null)
                partitions.Add(current);

            return partitions.ToArray();
        }

        /// <inheritdoc />
        public string ReAttach(string imagePath, string diskIdentifier, PartitionTableType tableType, bool readOnly = false)
        {
            // Unmount the disk first to ensure it's in a clean state
            Unmount(diskIdentifier);

            // Flush any pending writes and pull the disk offline
            FlushDisk(diskIdentifier);

            // Re-attach the disk image with the desired read-only state
            var script = $@"
                $image = Get-DiskImage -ImagePath '{imagePath}'
                if ($image) {{
                    $disk = $image | Get-Disk
                    # Pull disk online to repopulate Windows cache
                    if ($disk.IsOffline -Eq $true) {{
                        $disk | Set-Disk -IsOffline $false
                    }}
                    if ($image.Attached) {{
                        Dismount-DiskImage -ImagePath '{imagePath}'
                    }}
                    Mount-VHD -Path '{imagePath}' {(readOnly ? "-ReadOnly" : "")}
                }} else {{
                    throw 'Disk image not found: {imagePath}'
                }}
            ";
            var output = RunPowerShell(script);

            // Wait for the disk to be attached and get the new disk number
            int newDiskNumber = WaitForDiskAttachment(imagePath, TimeSpan.FromSeconds(5));
            if (newDiskNumber < 0)
                throw new InvalidOperationException($"Failed to re-attach VHD: {imagePath}");

            return $@"\\.\PhysicalDrive{newDiskNumber}";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_sessionLock)
            {
                _session?.Dispose();
                _session = null;
            }
        }
    }
}
