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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// The kind of supervisor that controls the lifetime of the host process
/// </summary>
public enum SupervisorKind
{
    /// <summary>
    /// No supervisor; the process was started by a user, a shortcut or a login item
    /// </summary>
    None,
    /// <summary>
    /// A systemd unit; the unit restarts the process when it exits
    /// </summary>
    Systemd,
    /// <summary>
    /// A launchd agent
    /// </summary>
    Launchd,
    /// <summary>
    /// A Windows service hosting the server in-process
    /// </summary>
    WindowsService,
    /// <summary>
    /// The duplicati-service wrapper, which spawns the server and respawns it on exit
    /// </summary>
    ServiceWrapper
}

/// <summary>
/// Describes how the running server is hosted, and how it can be stopped and started again
/// </summary>
/// <param name="Origin">The origin reported by the application settings, e.g. "Server", "Tray icon" or "Agent"</param>
/// <param name="Supervisor">The supervisor controlling the process</param>
/// <param name="ParentPid">The parent process id, or -1</param>
/// <param name="ProcessPath">The path of the running executable</param>
/// <param name="Arguments">The command line arguments, without the executable</param>
/// <param name="SystemdUnit">The systemd unit, when supervised by systemd</param>
/// <param name="SystemdUserUnit">True if the systemd unit is a user unit</param>
/// <param name="SystemdRunPath">The path to systemd-run, when available</param>
/// <param name="LaunchdLabel">The launchd label, when known</param>
/// <param name="LaunchdUid">The user id for the launchd GUI domain</param>
/// <param name="WindowsServiceName">The Windows service name, when hosted by a service</param>
/// <param name="AppImagePath">The AppImage path, when running from an AppImage</param>
/// <param name="InteractiveSession">True if a graphical privilege prompt can be shown</param>
public sealed record HostEnvironment(
    string Origin,
    SupervisorKind Supervisor,
    int ParentPid,
    string ProcessPath,
    string[] Arguments,
    string? SystemdUnit,
    bool SystemdUserUnit,
    string? SystemdRunPath,
    string? LaunchdLabel,
    int LaunchdUid,
    string? WindowsServiceName,
    string? AppImagePath,
    bool InteractiveSession)
{
    /// <summary>
    /// Environment variable set by a host process to declare how the server is hosted.
    /// Recognized values are <see cref="HostKindWindowsService"/> and <see cref="HostKindServiceWrapper"/>;
    /// unset means the server was started directly. systemd and launchd are detected from their own variables.
    /// </summary>
    public const string HostKindEnvName = "DUPLICATI_HOST_KIND";
    /// <summary>
    /// Environment variable set by a host process with its own process id, when the host is a separate process
    /// </summary>
    public const string HostPidEnvName = "DUPLICATI_HOST_PID";
    /// <summary>
    /// Environment variable set by a host process with its name, e.g. the Windows service name
    /// </summary>
    public const string HostNameEnvName = "DUPLICATI_HOST_NAME";
    /// <summary>
    /// The value of <see cref="HostKindEnvName"/> for a Windows service hosting the server in-process
    /// </summary>
    public const string HostKindWindowsService = "windows-service";
    /// <summary>
    /// The value of <see cref="HostKindEnvName"/> for the duplicati-service wrapper spawning the server
    /// </summary>
    public const string HostKindServiceWrapper = "service-wrapper";
    /// <summary>
    /// The option the service wrapper adds to the server command line
    /// </summary>
    public const string PingPongOption = "--ping-pong-keepalive";
    /// <summary>
    /// The line the server writes to stdout to tell the service wrapper not to respawn it
    /// </summary>
    public const string ServiceWrapperUpdatePendingLine = "update-pending";
    /// <summary>
    /// The origin reported by the tray icon
    /// </summary>
    public const string TrayIconOrigin = "Tray icon";
    /// <summary>
    /// The origin reported by the agent
    /// </summary>
    public const string AgentOrigin = "Agent";

    /// <summary>
    /// Detects the host environment of the current process
    /// </summary>
    /// <param name="origin">The origin from the application settings</param>
    /// <param name="arguments">The command line arguments, without the executable</param>
    /// <returns>The detected environment</returns>
    public static HostEnvironment Detect(string origin, IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        var processPath = Environment.ProcessPath ?? "";
        var isAgent = string.Equals(origin, AgentOrigin, StringComparison.OrdinalIgnoreCase);
        var isTray = string.Equals(origin, TrayIconOrigin, StringComparison.OrdinalIgnoreCase);

        var supervisor = SupervisorKind.None;
        var parentPid = -1;
        string? systemdUnit = null;
        var systemdUser = false;
        string? systemdRun = null;
        string? launchdLabel = null;
        var launchdUid = ProcessInfo.UnixUserId();
        string? serviceName = null;

        var hostKind = Environment.GetEnvironmentVariable(HostKindEnvName);
        var hostPid = Environment.GetEnvironmentVariable(HostPidEnvName);
        var hostName = Environment.GetEnvironmentVariable(HostNameEnvName);

        if (string.Equals(hostKind, HostKindWindowsService, StringComparison.OrdinalIgnoreCase)
            || (OperatingSystem.IsWindows() && !Environment.UserInteractive))
        {
            supervisor = SupervisorKind.WindowsService;
            serviceName = string.IsNullOrWhiteSpace(hostName)
                ? (isAgent ? "Duplicati.Agent" : "Duplicati")
                : hostName;
        }
        else if (string.Equals(hostKind, HostKindServiceWrapper, StringComparison.OrdinalIgnoreCase))
        {
            supervisor = SupervisorKind.ServiceWrapper;
            parentPid = int.TryParse(hostPid, out var p) ? p : ProcessInfo.GetParentProcessId();
        }
        else if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVOCATION_ID")))
        {
            supervisor = SupervisorKind.Systemd;
            (systemdUnit, systemdUser) = DetectSystemdUnit(isAgent);
            systemdRun = FindExecutable("systemd-run");
        }
        else if (OperatingSystem.IsMacOS() && IsLaunchdLabel(Environment.GetEnvironmentVariable("XPC_SERVICE_NAME")))
        {
            supervisor = SupervisorKind.Launchd;
            launchdLabel = Environment.GetEnvironmentVariable("XPC_SERVICE_NAME");
        }

        if (OperatingSystem.IsMacOS() && launchdLabel == null)
            launchdLabel = isAgent ? "com.duplicati.agent.launchagent" : isTray ? "com.duplicati.app.launchagent" : "com.duplicati.server.launchagent";

        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImage))
            appImage = null;

        var interactive = supervisor switch
        {
            SupervisorKind.WindowsService => false,
            SupervisorKind.Systemd => false,
            _ => OperatingSystem.IsWindows()
                ? Environment.UserInteractive
                : OperatingSystem.IsMacOS()
                    ? string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
                    : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        };

        return new HostEnvironment(origin, supervisor, parentPid, processPath, args, systemdUnit, systemdUser, systemdRun, launchdLabel, launchdUid, serviceName, appImage, interactive);
    }

    /// <summary>
    /// True if the server must exit by itself after the helper has been told to go
    /// </summary>
    public bool ServerExitsBySelf => Supervisor != SupervisorKind.Systemd && Supervisor != SupervisorKind.ServiceWrapper;

    /// <summary>
    /// True if the server should wait for its parent to be terminated by the helper, then exit
    /// </summary>
    public bool ServerExitsWhenParentExits => Supervisor == SupervisorKind.ServiceWrapper && ParentPid > 0;

    /// <summary>
    /// The process ids the helper must wait for
    /// </summary>
    public int[] GetWaitPids()
        => Supervisor == SupervisorKind.ServiceWrapper && ParentPid > 0
            ? [Environment.ProcessId, ParentPid]
            : [Environment.ProcessId];

    /// <summary>
    /// The process ids the helper must terminate, because they would respawn the old version
    /// </summary>
    public int[] GetTerminatePids()
        => Supervisor == SupervisorKind.ServiceWrapper && ParentPid > 0
            ? [ParentPid]
            : [];

    /// <summary>
    /// Creates the instruction the helper uses to bring the application back
    /// </summary>
    public RestartInstruction CreateRestartInstruction()
    {
        switch (Supervisor)
        {
            case SupervisorKind.Systemd:
                return new RestartInstruction { Kind = UpdateHelperProtocol.RestartSystemd, SystemdUnit = SystemdUnit, SystemdUserUnit = SystemdUserUnit };

            case SupervisorKind.Launchd:
                return new RestartInstruction { Kind = UpdateHelperProtocol.RestartLaunchd, LaunchdLabel = LaunchdLabel, LaunchdUid = LaunchdUid };

            case SupervisorKind.WindowsService:
                return new RestartInstruction { Kind = UpdateHelperProtocol.RestartWindowsService, WindowsServiceName = WindowsServiceName };

            case SupervisorKind.ServiceWrapper:
                {
                    var wrapper = ParentPid > 0 ? ProcessInfo.GetProcessPath(ParentPid) : null;
                    if (string.IsNullOrWhiteSpace(wrapper) || !File.Exists(wrapper))
                        wrapper = Path.Combine(UpdaterManager.INSTALLATIONDIR, PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.Service));

                    return new RestartInstruction
                    {
                        Kind = UpdateHelperProtocol.RestartSpawn,
                        Executable = wrapper,
                        Arguments = Arguments.Where(x => !x.StartsWith(PingPongOption, StringComparison.OrdinalIgnoreCase)).ToArray(),
                        WorkingDirectory = Path.GetDirectoryName(wrapper),
                        LaunchdLabel = LaunchdLabel,
                        LaunchdUid = LaunchdUid,
                    };
                }

            default:
                return new RestartInstruction
                {
                    Kind = string.IsNullOrWhiteSpace(ProcessPath) ? UpdateHelperProtocol.RestartNone : UpdateHelperProtocol.RestartSpawn,
                    Executable = AppImagePath ?? ProcessPath,
                    Arguments = Arguments,
                    WorkingDirectory = Environment.CurrentDirectory,
                    LaunchdLabel = LaunchdLabel,
                    LaunchdUid = LaunchdUid,
                };
        }
    }

    /// <summary>
    /// A one-line description for logs
    /// </summary>
    public string Describe()
        => $"origin={Origin}, supervisor={Supervisor}, parent={ParentPid}, interactive={InteractiveSession}, systemd={SystemdUnit ?? "-"}, launchd={LaunchdLabel ?? "-"}, service={WindowsServiceName ?? "-"}, appimage={AppImagePath ?? "-"}";

    private static bool IsLaunchdLabel(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("com.duplicati.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the unit name from the cgroup of the process
    /// </summary>
    internal static (string Unit, bool UserUnit) DetectSystemdUnit(bool isAgent)
    {
        var fallback = isAgent ? "duplicati-agent.service" : "duplicati.service";
        try
        {
            foreach (var line in File.ReadAllLines("/proc/self/cgroup"))
            {
                var path = line.Substring(line.LastIndexOf(':') + 1);
                var parsed = ParseSystemdUnitFromCgroupPath(path);
                if (parsed != null)
                    return parsed.Value;
            }
        }
        catch
        {
        }

        return (fallback, false);
    }

    /// <summary>
    /// Finds the service unit in a cgroup path such as <c>/system.slice/duplicati.service</c>
    /// </summary>
    internal static (string Unit, bool UserUnit)? ParseSystemdUnitFromCgroupPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var unit = segments.LastOrDefault(x => x.EndsWith(".service", StringComparison.Ordinal));
        if (unit == null)
            return null;

        var userUnit = segments.Any(x => x.StartsWith("user@", StringComparison.Ordinal));
        return (unit, userUnit);
    }

    private static string? FindExecutable(string name)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/local/bin", "/usr/bin", "/bin"]);
        return dirs.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
    }
}
