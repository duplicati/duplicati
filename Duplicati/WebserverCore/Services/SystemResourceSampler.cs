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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.Logging;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Samples resource usage for the machine and the Duplicati processes.
/// Only the previous sample is kept, as it is needed to compute CPU usage.
/// </summary>
public class SystemResourceSampler : ISystemResourceSampler
{
    private static readonly string LOGTAG = Log.LogTagFromType<SystemResourceSampler>();

    /// <summary>
    /// The minimum time between two samples used to compute CPU usage;
    /// samples closer together return the previously computed value
    /// </summary>
    private static readonly TimeSpan MinimumCpuSampleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The previous CPU sample for a process
    /// </summary>
    private sealed record ProcessCpuSample(DateTime Timestamp, TimeSpan TotalProcessorTime, double? CpuUsage);

    /// <summary>
    /// The previous machine-wide CPU sample
    /// </summary>
    private sealed record MachineCpuSample(DateTime Timestamp, ulong BusyTicks, ulong TotalTicks, double? CpuUsage);

    private readonly object _lock = new();
    private readonly Dictionary<int, ProcessCpuSample> _processSamples = new();
    private MachineCpuSample? _machineSample;
    private bool _machineCpuUnavailable;

    /// <inheritdoc />
    public SystemStatusMachineDto SampleMachine()
    {
        lock (_lock)
        {
            var (totalMemory, availableMemory) = GetMemory();
            return new SystemStatusMachineDto(
                ProcessorCount: Environment.ProcessorCount,
                CpuUsage: SampleMachineCpu(),
                TotalMemory: totalMemory,
                AvailableMemory: availableMemory
            );
        }
    }

    /// <inheritdoc />
    public IEnumerable<SystemStatusProcessDto> SampleProcesses()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var result = new List<SystemStatusProcessDto>();

            using (var current = Process.GetCurrentProcess())
            {
                var sample = SampleProcess(current, "server", now, GC.GetTotalMemory(false));
                if (sample != null)
                    result.Add(sample);
            }

            foreach (var pid in Library.Main.IPC.ControllerRpcProxy.ActiveProcessIds)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    var sample = SampleProcess(process, "controller", now, null);
                    if (sample != null)
                        result.Add(sample);
                }
                catch
                {
                    // The process has exited
                }
            }

            // Forget processes that are no longer reported
            var seen = result.Select(x => x.ProcessId).ToHashSet();
            foreach (var pid in _processSamples.Keys.Where(x => !seen.Contains(x)).ToList())
                _processSamples.Remove(pid);

            return result;
        }
    }

    /// <summary>
    /// Samples a single process
    /// </summary>
    /// <param name="process">The process to sample</param>
    /// <param name="role">The role of the process</param>
    /// <param name="now">The time of the sample</param>
    /// <param name="managedHeapSize">The managed heap size, if known</param>
    /// <returns>The sample, or null if the process could not be sampled</returns>
    private SystemStatusProcessDto? SampleProcess(Process process, string role, DateTime now, long? managedHeapSize)
    {
        try
        {
            process.Refresh();
            if (process.HasExited)
                return null;

            var cpuTime = process.TotalProcessorTime;
            double? cpuUsage = null;
            if (_processSamples.TryGetValue(process.Id, out var previous))
            {
                var elapsed = now - previous.Timestamp;
                if (elapsed < MinimumCpuSampleInterval)
                {
                    cpuUsage = previous.CpuUsage;
                    cpuTime = previous.TotalProcessorTime;
                    now = previous.Timestamp;
                }
                else
                {
                    cpuUsage = Math.Clamp((cpuTime - previous.TotalProcessorTime).TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount), 0, 1);
                }
            }

            _processSamples[process.Id] = new ProcessCpuSample(now, cpuTime, cpuUsage);

            DateTime? startTime = null;
            try { startTime = process.StartTime.ToUniversalTime(); }
            catch { }

            return new SystemStatusProcessDto(
                Role: role,
                ProcessId: process.Id,
                StartTime: startTime,
                CpuUsage: cpuUsage,
                WorkingSet: process.WorkingSet64,
                PrivateMemory: process.PrivateMemorySize64,
                ManagedHeapSize: managedHeapSize,
                ThreadCount: process.Threads.Count
            );
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LOGTAG, "ProcessSampleFailed", ex, "Failed to sample process {0}", process.Id);
            return null;
        }
    }

    /// <summary>
    /// Samples the machine-wide CPU usage
    /// </summary>
    /// <returns>The CPU usage as a fraction between 0 and 1, or null if not available</returns>
    private double? SampleMachineCpu()
    {
        if (_machineCpuUnavailable)
            return null;

        (ulong Busy, ulong Total)? ticks;
        try
        {
            ticks = GetMachineCpuTicks();
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LOGTAG, "MachineCpuSampleFailed", ex, "Failed to sample machine CPU usage");
            ticks = null;
        }

        if (ticks == null)
        {
            _machineCpuUnavailable = true;
            return null;
        }

        var now = DateTime.UtcNow;
        var previous = _machineSample;

        // The tick counters are not updated continuously on all platforms, so with several
        // clients sampling close together, keep the baseline until the counters have advanced
        if (previous != null && (now - previous.Timestamp < MinimumCpuSampleInterval || ticks.Value.Total <= previous.TotalTicks))
            return previous.CpuUsage;

        double? cpuUsage = null;
        if (previous != null)
            cpuUsage = Math.Clamp((double)(ticks.Value.Busy - Math.Min(ticks.Value.Busy, previous.BusyTicks)) / (ticks.Value.Total - previous.TotalTicks), 0, 1);

        _machineSample = new MachineCpuSample(now, ticks.Value.Busy, ticks.Value.Total, cpuUsage);
        return cpuUsage;
    }

    /// <summary>
    /// Gets the cumulative busy and total CPU ticks for the machine
    /// </summary>
    /// <returns>The ticks, or null if not supported on this platform</returns>
    private static (ulong Busy, ulong Total)? GetMachineCpuTicks()
    {
        if (OperatingSystem.IsLinux())
        {
            // First line: cpu user nice system idle iowait irq softirq steal ...
            var line = File.ReadLines("/proc/stat").FirstOrDefault(x => x.StartsWith("cpu "));
            if (line == null)
                return null;

            var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(ulong.Parse).ToArray();
            if (values.Length < 5)
                return null;

            var total = values.Aggregate(0UL, (a, b) => a + b);
            var idle = values[3] + values[4];
            return (total - idle, total);
        }

        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return null;

            // Kernel time includes idle time
            var total = kernel + user;
            return (total - idle, total);
        }

        if (OperatingSystem.IsMacOS())
        {
            var info = new uint[HOST_CPU_LOAD_INFO_COUNT];
            var count = (uint)info.Length;
            if (host_statistics(MachHost.Value, HOST_CPU_LOAD_INFO, info, ref count) != 0)
                return null;

            // user, system, idle, nice
            var total = (ulong)info[0] + info[1] + info[2] + info[3];
            return (total - info[2], total);
        }

        return null;
    }

    /// <summary>
    /// Gets the total and available memory for the machine
    /// </summary>
    /// <returns>The total and available memory, either may be null if not available</returns>
    private static (long? Total, long? Available) GetMemory()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                long? total = null, available = null;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:"))
                        total = ParseMeminfoKb(line);
                    else if (line.StartsWith("MemAvailable:"))
                        available = ParseMeminfoKb(line);
                }

                if (total != null)
                    return (total, available);
            }
            else if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                    return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var available = GetMacAvailableMemory();
                if (available != null)
                    return (GetMacTotalMemory(), available);
            }
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LOGTAG, "MachineMemorySampleFailed", ex, "Failed to sample machine memory");
        }

        // The runtime knows the physical memory (or container limit) on all platforms
        var gcTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (gcTotal > 0 ? gcTotal : null, null);
    }

    /// <summary>
    /// Gets the available memory on macOS, counted as free and inactive pages, which is what the system can hand out without paging
    /// </summary>
    /// <returns>The available memory in bytes, or null if it could not be determined</returns>
    [SupportedOSPlatform("macos")]
    private static long? GetMacAvailableMemory()
    {
        var info = new uint[HOST_VM_INFO64_COUNT];
        var count = (uint)info.Length;
        if (host_statistics64(MachHost.Value, HOST_VM_INFO64, info, ref count) != 0)
            return null;
        if (host_page_size(MachHost.Value, out var pageSize) != 0)
            return null;

        // vm_statistics64: free_count is the first field and inactive_count the third
        return ((long)info[0] + info[2]) * (long)pageSize;
    }

    /// <summary>
    /// Gets the installed memory on macOS
    /// </summary>
    /// <returns>The installed memory in bytes, or null if it could not be determined</returns>
    [SupportedOSPlatform("macos")]
    private static long? GetMacTotalMemory()
    {
        long value = 0;
        var size = (nint)sizeof(long);
        return sysctlbyname("hw.memsize", ref value, ref size, IntPtr.Zero, 0) == 0 && value > 0 ? value : null;
    }

    /// <summary>
    /// Parses a line from /proc/meminfo, such as "MemTotal:  16314788 kB"
    /// </summary>
    /// <param name="line">The line to parse</param>
    /// <returns>The value in bytes, or null if it could not be parsed</returns>
    private static long? ParseMeminfoKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var kb) ? kb * 1024 : null;
    }

    #region Native methods

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);

    private const int HOST_CPU_LOAD_INFO = 3;
    private const int HOST_CPU_LOAD_INFO_COUNT = 4;

    [SupportedOSPlatform("macos")]
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    /// <summary>
    /// The host port, cached as each call to mach_host_self allocates a port right
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static readonly Lazy<uint> MachHost = new(mach_host_self);

    [SupportedOSPlatform("macos")]
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, [Out] uint[] hostInfo, ref uint hostInfoCount);

    private const int HOST_VM_INFO64 = 4;
    /// <summary>
    /// The size of vm_statistics64 in 32-bit units
    /// </summary>
    private const int HOST_VM_INFO64_COUNT = 38;

    [SupportedOSPlatform("macos")]
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics64(uint host, int flavor, [Out] uint[] hostInfo, ref uint hostInfoCount);

    [SupportedOSPlatform("macos")]
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_page_size(uint host, out nuint pageSize);

    [SupportedOSPlatform("macos")]
    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int sysctlbyname(string name, ref long oldp, ref nint oldlenp, IntPtr newp, nint newlen);

    #endregion
}
