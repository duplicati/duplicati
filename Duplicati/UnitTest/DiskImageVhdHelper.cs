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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Helper class for managing VHD (Virtual Hard Disk) files using PowerShell.
    /// This class provides methods to create, attach, format, and detach VHD files
    /// for testing disk image backup and restore operations.
    /// </summary>
    internal static class DiskImageVhdHelper
    {
        /// <summary>
        /// Creates a VHD file, attaches it to the system, and returns the physical drive path.
        /// </summary>
        /// <param name="vhdPath">The path where the VHD file will be created.</param>
        /// <param name="sizeMB">The size of the VHD in megabytes.</param>
        /// <returns>The physical drive path (e.g., \\.\PhysicalDriveN).</returns>
        public static string CreateAndAttachVhd(string vhdPath, long sizeMB)
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(vhdPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Delete existing VHD if it exists
            if (File.Exists(vhdPath))
            {
                try
                {
                    DetachVhd(vhdPath);
                    File.Delete(vhdPath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            // Create the VHD using PowerShell
            var script = $@"New-VHD -Path '{vhdPath}' -SizeBytes {sizeMB}MB -Fixed | Out-Null; Mount-DiskImage -ImagePath '{vhdPath}' | Out-Null";
            RunPowerShell(script);

            // Wait for the disk to be attached and get the disk number
            int diskNumber = WaitForDiskAttachment(vhdPath, TimeSpan.FromSeconds(30));
            if (diskNumber < 0)
            {
                throw new InvalidOperationException($"Failed to get disk number for VHD: {vhdPath}");
            }

            return $"\\\\.\\PhysicalDrive{diskNumber}";
        }

        /// <summary>
        /// Waits for a VHD to be attached and returns its disk number.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The disk number, or -1 if not found within the timeout.</returns>
        private static int WaitForDiskAttachment(string vhdPath, TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                var diskNumber = GetDiskNumber(vhdPath);
                if (diskNumber >= 0)
                {
                    return diskNumber;
                }
                Thread.Sleep(100);
            }
            return -1;
        }

        /// <summary>
        /// Initializes a disk with the specified partition table type (GPT or MBR).
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        public static void InitializeDisk(int diskNumber, string tableType)
        {
            var partitionStyle = tableType.ToLowerInvariant() switch
            {
                "gpt" => "GPT",
                "mbr" => "MBR",
                _ => throw new ArgumentException($"Invalid partition table type: {tableType}", nameof(tableType))
            };

            var script = $@"
                $disk = Get-Disk -Number {diskNumber}
                if ($disk.PartitionStyle -ne 'RAW') {{
                    Clear-Disk -Number {diskNumber} -RemoveData -Confirm:$false
                }}
                Initialize-Disk -Number {diskNumber} -PartitionStyle {partitionStyle}
            ";
            RunPowerShell(script);

            // Wait for the initialization to complete
            WaitForDiskInitialization(diskNumber, TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Waits for a disk to be initialized.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        private static void WaitForDiskInitialization(int diskNumber, TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                var script = $"(Get-Disk -Number {diskNumber}).PartitionStyle";
                var result = RunPowerShell(script)?.Trim();
                if (result == "GPT" || result == "MBR")
                {
                    return;
                }
                Thread.Sleep(100);
            }
            throw new TimeoutException($"Disk {diskNumber} initialization timed out");
        }

        /// <summary>
        /// Creates a partition on the specified disk, formats it with the specified filesystem,
        /// and assigns a drive letter.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="fsType">The filesystem type ("ntfs" or "fat32").</param>
        /// <param name="sizeMB">The size of the partition in MB (0 for all available space).</param>
        /// <returns>The assigned drive letter.</returns>
        public static char CreateAndFormatPartition(int diskNumber, string fsType, long sizeMB = 0)
        {
            var fsTypeUpper = fsType.ToUpperInvariant();

            // Find an available drive letter
            char driveLetter = FindAvailableDriveLetter();

            var sizeParam = sizeMB > 0 ? $"-Size {sizeMB}MB" : "-UseMaximumSize";

            var script = $@"
                $partition = New-Partition -DiskNumber {diskNumber} {sizeParam} -AssignDriveLetter
                # Wait for the partition to be ready
                $timeout = (Get-Date).AddSeconds(30)
                while ((Get-Date) -lt $timeout) {{
                    $vol = Get-Partition -DiskNumber {diskNumber} | Where-Object {{ $_.PartitionNumber -eq $partition.PartitionNumber }} | Get-Volume
                    if ($vol) {{ break }}
                    Start-Sleep -Milliseconds 100
                }}
                # Format the volume
                $vol = Format-Volume -Partition $partition -FileSystem {fsTypeUpper} -NewFileSystemLabel 'TestVol' -Confirm:$false
                # Get the assigned drive letter
                (Get-Partition -DiskNumber {diskNumber} | Where-Object {{ $_.PartitionNumber -eq $partition.PartitionNumber }} | Get-Volume).DriveLetter
            ";

            var result = RunPowerShell(script);
            var assignedLetter = result?.Trim();

            if (!string.IsNullOrEmpty(assignedLetter) && assignedLetter.Length == 1 && char.IsLetter(assignedLetter[0]))
            {
                return char.ToUpperInvariant(assignedLetter[0]);
            }

            // Fallback: try to find the drive letter by checking the disk
            return FindDriveLetterForDisk(diskNumber);
        }

        /// <summary>
        /// Finds the drive letter assigned to a disk.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>The drive letter, or throws if not found.</returns>
        private static char FindDriveLetterForDisk(int diskNumber)
        {
            var script = $@"
                Get-Partition -DiskNumber {diskNumber} | Get-Volume | Where-Object {{ $_.DriveLetter -ne $null }} | Select-Object -ExpandProperty DriveLetter
            ";
            var result = RunPowerShell(script)?.Trim();

            if (!string.IsNullOrEmpty(result) && result.Length >= 1 && char.IsLetter(result[0]))
            {
                return char.ToUpperInvariant(result[0]);
            }

            throw new InvalidOperationException($"Could not find drive letter for disk {diskNumber}");
        }

        /// <summary>
        /// Populates a drive with test data including files of various sizes and directory structures.
        /// </summary>
        /// <param name="driveLetter">The drive letter to populate.</param>
        /// <param name="fileCount">The number of files to create.</param>
        /// <param name="fileSizeKB">The size of each file in KB.</param>
        public static void PopulateTestData(char driveLetter, int fileCount = 10, int fileSizeKB = 10)
        {
            var drivePath = $"{driveLetter}:\\";

            // Create a small text file
            File.WriteAllText(Path.Combine(drivePath, "testfile_small.txt"),
                "This is a small test file for disk image backup testing.\n" +
                "It contains simple text data.\n" +
                new string('=', 100));

            // Create a medium binary file with random data
            var random = new Random(42); // Fixed seed for reproducibility
            var mediumData = new byte[fileSizeKB * 1024];
            random.NextBytes(mediumData);
            File.WriteAllBytes(Path.Combine(drivePath, "testfile_medium.bin"), mediumData);

            // Create nested directories with files
            var testDir = Path.Combine(drivePath, "testdir");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "nested_file.txt"),
                "This file is in a nested directory.\n" +
                $"Created at: {DateTime.UtcNow:O}\n");

            var subDir = Path.Combine(testDir, "subdir");
            Directory.CreateDirectory(subDir);
            var deepData = new byte[1024];
            random.NextBytes(deepData);
            File.WriteAllBytes(Path.Combine(subDir, "deep_file.bin"), deepData);

            // Create additional files if requested
            for (int i = 0; i < fileCount - 4; i++)
            {
                var fileData = new byte[fileSizeKB * 1024];
                random.NextBytes(fileData);
                File.WriteAllBytes(Path.Combine(drivePath, $"testfile_{i:D3}.bin"), fileData);
            }
        }

        /// <summary>
        /// Detaches a VHD file from the system.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        public static void DetachVhd(string vhdPath)
        {
            try
            {
                var script = $@"
                    $image = Get-DiskImage -ImagePath '{vhdPath}' -ErrorAction SilentlyContinue
                    if ($image -and $image.Attached) {{
                        Dismount-DiskImage -ImagePath '{vhdPath}'
                    }}
                ";
                RunPowerShell(script);
            }
            catch
            {
                // Ignore errors during detach - the disk may already be detached
            }
        }

        public static void UnmountForWriting(string vhdPath, char driveLetter)
        {
            // Remove the drive letter
            var diskNumber = GetDiskNumber(vhdPath);
            var script = $@"
                Get-Volume -Drive {driveLetter} | Get-Partition | Remove-PartitionAccessPath -AccessPath {driveLetter}:\
            ";
            RunPowerShell(script);

            // Pull the disk offline to ensure it's not in use
            script = $@"
                Set-Disk -Number {diskNumber} -IsOffline $true
            ";
            RunPowerShell(script);

            // Clear the readonly flag
            script = $@"
                Set-Disk -Number {diskNumber} -IsReadOnly $false
            ";
            RunPowerShell(script);
        }

        public static char MountForReading(string vhdPath, char? driveLetter = null)
        {
            driveLetter ??= FindAvailableDriveLetter();

            try
            {
                var diskNumber = GetDiskNumber(vhdPath);
                if (diskNumber >= 0)
                {
                    var script = $@"
                        Set-Disk -Number {diskNumber} -IsOffline $false
                        Get-Partition -DiskNumber {diskNumber} | Set-Partition -NewDriveLetter {driveLetter}
                    ";
                    RunPowerShell(script);
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to mount VHD for reading: {ex.Message}");
            }

            return driveLetter.Value;
        }

        /// <summary>
        /// Gets the disk number for an attached VHD.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <returns>The disk number, or -1 if not found.</returns>
        public static int GetDiskNumber(string vhdPath)
        {
            try
            {
                var script = $@"
                    $diskImage = Get-DiskImage -ImagePath '{vhdPath}' -ErrorAction SilentlyContinue
                    if ($diskImage -and $diskImage.Attached) {{
                        Get-Disk | Where-Object {{ $_.Path -like '*{Path.GetFileName(vhdPath)}*' -or ($_ | Get-DiskImage).ImagePath -eq '{vhdPath}' }} | Select-Object -ExpandProperty Number
                    }}
                ";
                var result = RunPowerShell(script)?.Trim();

                if (!string.IsNullOrEmpty(result) && int.TryParse(result, out int diskNumber))
                {
                    return diskNumber;
                }

                // Alternative approach: query by disk image path
                script = $@"
                    Get-DiskImage -ImagePath '{vhdPath}' | Get-Disk | Select-Object -ExpandProperty Number
                ";
                result = RunPowerShell(script)?.Trim();

                if (!string.IsNullOrEmpty(result) && int.TryParse(result, out diskNumber))
                {
                    return diskNumber;
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: PowerShell GetDiskNumber failed: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Runs a PowerShell script and returns the output.
        /// </summary>
        /// <param name="script">The PowerShell script to execute.</param>
        /// <returns>The output from PowerShell.</returns>
        public static string RunPowerShell(string script)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{EscapeForCommandLine(script)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas" // Request elevation
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start PowerShell process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"PowerShell failed with exit code {process.ExitCode}: {error}\nScript:\n{script}");
            }

            return output;
        }

        /// <summary>
        /// Escapes a PowerShell script for use on the command line.
        /// </summary>
        /// <param name="script">The script to escape.</param>
        /// <returns>The escaped script.</returns>
        private static string EscapeForCommandLine(string script)
        {
            // Replace double quotes with escaped double quotes for command line
            return script.Replace("\"", "\\\"");
        }

        /// <summary>
        /// Checks if the current process is running with administrator privileges.
        /// </summary>
        /// <returns>True if running as administrator, false otherwise.</returns>
        public static bool IsAdministrator()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

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

        /// <summary>
        /// Finds an available drive letter that is not currently in use.
        /// </summary>
        /// <returns>An available drive letter.</returns>
        private static char FindAvailableDriveLetter()
        {
            var usedDrives = DriveInfo.GetDrives()
                .Where(d => d.Name.Length >= 2)
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();

            // Try letters from Z down to D (avoid A, B, C which are typically reserved)
            for (char c = 'Z'; c >= 'D'; c--)
            {
                if (!usedDrives.Contains(c))
                {
                    return c;
                }
            }

            throw new InvalidOperationException("No available drive letters found.");
        }

        /// <summary>
        /// Cleans up and deletes a VHD file, ensuring it is detached first.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        public static void CleanupVhd(string vhdPath)
        {
            try
            {
                DetachVhd(vhdPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to detach VHD {vhdPath}: {ex.Message}");
            }

            try
            {
                if (File.Exists(vhdPath))
                {
                    File.Delete(vhdPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to delete VHD file {vhdPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets detailed information about a disk using PowerShell.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>The PowerShell output for the disk details.</returns>
        public static string GetDiskDetails(int diskNumber)
        {
            var script = $@"
                Write-Output ""Disk Details for Disk {diskNumber}:""
                Write-Output ""================================""
                Get-Disk -Number {diskNumber} | Format-List
                Write-Output ''
                Write-Output ""Partitions:""
                Write-Output ""===========""
                Get-Partition -DiskNumber {diskNumber} | Format-Table
            ";
            return RunPowerShell(script);
        }

        /// <summary>
        /// Gets the volume information for a drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter.</param>
        /// <returns>The volume information.</returns>
        public static string GetVolumeInfo(char driveLetter)
        {
            var script = $@"
                Get-Volume -DriveLetter {driveLetter} | Format-List
            ";
            return RunPowerShell(script);
        }
    }
}
