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
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Helper class for managing VHD (Virtual Hard Disk) files using diskpart.
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

            // Create the VHD using diskpart
            var script = $@"create vdisk file=""{vhdPath}"" maximum={sizeMB} type=expandable
attach vdisk";
            RunDiskpart(script);

            // Wait a moment for the disk to be attached
            Thread.Sleep(500);

            // Get the disk number
            int diskNumber = GetDiskNumber(vhdPath);
            if (diskNumber < 0)
            {
                throw new InvalidOperationException($"Failed to get disk number for VHD: {vhdPath}");
            }

            return $"\\\\.\\PhysicalDrive{diskNumber}";
        }

        /// <summary>
        /// Initializes a disk with the specified partition table type (GPT or MBR).
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        public static void InitializeDisk(int diskNumber, string tableType)
        {
            var script = $@"select disk {diskNumber}
convert {tableType.ToLowerInvariant()}";
            RunDiskpart(script);

            // Wait for the conversion to complete
            Thread.Sleep(200);
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
            var sizeArg = sizeMB > 0 ? $"size={sizeMB}" : "";
            var fsTypeLower = fsType.ToLowerInvariant();

            // Find an available drive letter
            char driveLetter = FindAvailableDriveLetter();

            var script = $@"select disk {diskNumber}
create partition primary {sizeArg}
format fs={fsTypeLower} quick
assign letter={driveLetter}";

            RunDiskpart(script);

            // Wait for formatting to complete
            Thread.Sleep(500);

            return driveLetter;
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
                var script = $@"select vdisk file=""{vhdPath}""
detach vdisk";
                RunDiskpart(script);
            }
            catch
            {
                // Ignore errors during detach - the disk may already be detached
            }
        }

        /// <summary>
        /// Gets the disk number for an attached VHD.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <returns>The disk number, or -1 if not found.</returns>
        public static int GetDiskNumber(string vhdPath)
        {
            // Use diskpart to list vdisks and find our disk number
            var output = RunDiskpart("list vdisk");

            // Parse the output to find the disk number
            // The output format includes the VHD file path and associated disk number
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.Contains(vhdPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Try to extract disk number from the line
                    // Format typically includes: Disk ###, State, Type, File
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].StartsWith("Disk", StringComparison.OrdinalIgnoreCase) &&
                            i + 1 < parts.Length)
                        {
                            var diskNumStr = parts[i + 1].Trim();
                            if (int.TryParse(diskNumStr, out int diskNumber))
                            {
                                return diskNumber;
                            }
                        }
                    }
                }
            }

            // Alternative approach: use WMI or check diskpart list disk output
            // Try to find the disk by looking for recently attached disks
            var listDiskOutput = RunDiskpart("list disk");
            var diskLines = listDiskOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Find the highest disk number (usually the most recently attached VHD)
            int maxDiskNumber = -1;
            foreach (var line in diskLines)
            {
                if (line.StartsWith("  Disk ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int diskNum))
                    {
                        if (diskNum > maxDiskNumber)
                        {
                            maxDiskNumber = diskNum;
                        }
                    }
                }
            }

            return maxDiskNumber;
        }

        /// <summary>
        /// Runs a diskpart script and returns the output.
        /// </summary>
        /// <param name="script">The diskpart script to execute.</param>
        /// <returns>The output from diskpart.</returns>
        public static string RunDiskpart(string script)
        {
            var scriptPath = Path.GetTempFileName() + ".txt";
            try
            {
                File.WriteAllText(scriptPath, script, Encoding.ASCII);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart.exe",
                        Arguments = $"/s \"{scriptPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas" // Request elevation
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException($"diskpart failed with exit code {process.ExitCode}: {error}\nScript:\n{script}");
                }

                return output;
            }
            finally
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
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
        /// Gets detailed information about a disk using diskpart.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>The diskpart output for the disk details.</returns>
        public static string GetDiskDetails(int diskNumber)
        {
            var script = $@"select disk {diskNumber}
detail disk";
            return RunDiskpart(script);
        }

        /// <summary>
        /// Gets the volume information for a drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter.</param>
        public static string GetVolumeInfo(char driveLetter)
        {
            var script = $@"select volume {driveLetter}
detail volume";
            return RunDiskpart(script);
        }
    }
}
