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

// This file is compiled into both Duplicati.Library.AutoUpdater (the server side)
// and Duplicati.UpdateHelper (the stand-alone helper). The helper has no
// reference to any Duplicati assembly, so the types are shared as source.
// The test project references both assemblies and reaches the helper copy
// through the extern alias "Helper".
namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Constants shared between the server and the update helper
/// </summary>
public static class UpdateHelperProtocol
{
    /// <summary>
    /// The protocol version; both sides refuse to talk to an unknown version
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The name of the folder below the data folder where updates are staged
    /// </summary>
    public const string UpdatesFolderName = "updates";
    /// <summary>
    /// The name of the file recording an install that is in progress
    /// </summary>
    public const string PendingRecordFilename = "pending-install.json";
    /// <summary>
    /// The name of the file the helper writes when it has finished
    /// </summary>
    public const string ResultFilename = "install-result.json";
    /// <summary>
    /// The prefix of the helper log file
    /// </summary>
    public const string HelperLogFilename = "helper.log";
    /// <summary>
    /// The prefix of the pipe the helper listens on for the started signal, followed by the helper pid.
    /// Pipe names are kept short: on macOS the socket path below the temp folder is limited to 104 characters.
    /// </summary>
    public const string HelperListenPipePrefix = "dup-upd-h";
    /// <summary>
    /// The prefix of the pipe the server listens on for the handshake, followed by a short random id
    /// </summary>
    public const string ServerPipePrefix = "dup-upd-";
    /// <summary>
    /// The prefix of the marker file the server writes when it cannot reach the helper pipe, followed by the helper pid
    /// </summary>
    public const string StartedMarkerPrefix = "started-";

    /// <summary>
    /// Sent by the helper after connecting
    /// </summary>
    public const string TypeHello = "hello";
    /// <summary>
    /// Sent by the server with the job description
    /// </summary>
    public const string TypeJob = "job";
    /// <summary>
    /// Sent by the helper when the job was verified and can be performed
    /// </summary>
    public const string TypeReady = "ready";
    /// <summary>
    /// Sent by the helper when the job cannot be performed
    /// </summary>
    public const string TypeAbort = "abort";
    /// <summary>
    /// Sent by the server when the helper should proceed
    /// </summary>
    public const string TypeGo = "go";
    /// <summary>
    /// Sent by the new server instance once it is running
    /// </summary>
    public const string TypeStarted = "started";

    /// <summary>
    /// Restart by spawning the executable in the restart instruction
    /// </summary>
    public const string RestartSpawn = "spawn";
    /// <summary>
    /// Restart by starting a systemd unit
    /// </summary>
    public const string RestartSystemd = "systemd";
    /// <summary>
    /// Restart by kickstarting a launchd job
    /// </summary>
    public const string RestartLaunchd = "launchd";
    /// <summary>
    /// Restart by starting a Windows service
    /// </summary>
    public const string RestartWindowsService = "windows-service";
    /// <summary>
    /// Do not restart anything
    /// </summary>
    public const string RestartNone = "none";

    /// <summary>
    /// The environment variable the helper reads the pipe name from, when not given as an argument
    /// </summary>
    public const string PipeEnvironmentVariable = "DUPLICATI_UPDATE_PIPE";
}

/// <summary>
/// A message exchanged over the handshake pipes; one JSON object per line
/// </summary>
public sealed class HelperMessage
{
    /// <summary>
    /// The message type, one of the <c>Type*</c> constants
    /// </summary>
    public string Type { get; set; } = "";
    /// <summary>
    /// The protocol version of the sender
    /// </summary>
    public int ProtocolVersion { get; set; }
    /// <summary>
    /// The shared secret, sent with <see cref="UpdateHelperProtocol.TypeHello"/>
    /// </summary>
    public string? Secret { get; set; }
    /// <summary>
    /// The process id of the sender
    /// </summary>
    public int Pid { get; set; }
    /// <summary>
    /// The version of the sender
    /// </summary>
    public string? Version { get; set; }
    /// <summary>
    /// The reason, sent with <see cref="UpdateHelperProtocol.TypeAbort"/>
    /// </summary>
    public string? Reason { get; set; }
    /// <summary>
    /// The job description, sent with <see cref="UpdateHelperProtocol.TypeJob"/>
    /// </summary>
    public UpdateJob? Job { get; set; }
}

/// <summary>
/// Describes how the helper should bring the application back after installing
/// </summary>
public sealed class RestartInstruction
{
    /// <summary>
    /// One of the <c>Restart*</c> constants
    /// </summary>
    public string Kind { get; set; } = UpdateHelperProtocol.RestartNone;
    /// <summary>
    /// The executable to spawn, for <see cref="UpdateHelperProtocol.RestartSpawn"/>
    /// </summary>
    public string? Executable { get; set; }
    /// <summary>
    /// The arguments to pass when spawning
    /// </summary>
    public string[] Arguments { get; set; } = [];
    /// <summary>
    /// The working directory to use when spawning
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// The systemd unit name, for <see cref="UpdateHelperProtocol.RestartSystemd"/>
    /// </summary>
    public string? SystemdUnit { get; set; }
    /// <summary>
    /// True if the systemd unit is a user unit
    /// </summary>
    public bool SystemdUserUnit { get; set; }
    /// <summary>
    /// The launchd label, for <see cref="UpdateHelperProtocol.RestartLaunchd"/>; also used to unload the agent before a pkg install
    /// </summary>
    public string? LaunchdLabel { get; set; }
    /// <summary>
    /// The user id of the launchd GUI domain
    /// </summary>
    public int LaunchdUid { get; set; }
    /// <summary>
    /// The Windows service name, for <see cref="UpdateHelperProtocol.RestartWindowsService"/>
    /// </summary>
    public string? WindowsServiceName { get; set; }
}

/// <summary>
/// The job sent from the server to the helper
/// </summary>
public sealed class UpdateJob
{
    /// <summary>
    /// The full path to the downloaded package
    /// </summary>
    public string PackagePath { get; set; } = "";
    /// <summary>
    /// The expected length of the package in bytes
    /// </summary>
    public long PackageLength { get; set; }
    /// <summary>
    /// The expected SHA256 of the package, base64 encoded, as found in the signed manifest
    /// </summary>
    public string PackageSha256 { get; set; } = "";
    /// <summary>
    /// The package type id of the running installation, e.g. <c>linux-x64-gui.deb</c>
    /// </summary>
    public string PackageTypeId { get; set; } = "";
    /// <summary>
    /// The complete signed update manifest the package was taken from. The helper verifies the
    /// signature with its built-in keys and requires the package hash to be listed in it.
    /// </summary>
    public string SignedManifest { get; set; } = "";
    /// <summary>
    /// The folder the running installation lives in
    /// </summary>
    public string InstallDir { get; set; } = "";
    /// <summary>
    /// The version being installed
    /// </summary>
    public string TargetVersion { get; set; } = "";
    /// <summary>
    /// The version currently running
    /// </summary>
    public string CurrentVersion { get; set; } = "";
    /// <summary>
    /// Process ids that must have exited before the install can start
    /// </summary>
    public int[] WaitPids { get; set; } = [];
    /// <summary>
    /// Process ids the helper must terminate before waiting; used for supervisors that would respawn the old binary
    /// </summary>
    public int[] TerminatePids { get; set; } = [];
    /// <summary>
    /// Process names (without extension) the helper must terminate before installing; used to close the tray icon before an MSI upgrade in service mode
    /// </summary>
    public string[] TerminateProcessNames { get; set; } = [];
    /// <summary>
    /// How to restart the application after the install
    /// </summary>
    public RestartInstruction Restart { get; set; } = new();
    /// <summary>
    /// The path of the running AppImage, when the package type is appimage
    /// </summary>
    public string? AppImagePath { get; set; }
    /// <summary>
    /// Properties to pass to msiexec, when the package type is msi
    /// </summary>
    public Dictionary<string, string> MsiProperties { get; set; } = new();
    /// <summary>
    /// The file the helper writes its result to
    /// </summary>
    public string ResultFile { get; set; } = "";
    /// <summary>
    /// The file the helper writes its log to
    /// </summary>
    public string LogFile { get; set; } = "";
    /// <summary>
    /// The folder where the server writes the started marker file, if the helper pipe cannot be reached
    /// </summary>
    public string MarkerFolder { get; set; } = "";
    /// <summary>
    /// Seconds to wait for the old processes to exit
    /// </summary>
    public int WaitForExitTimeoutSeconds { get; set; } = 600;
    /// <summary>
    /// Seconds to wait for the new instance to report that it started
    /// </summary>
    public int WaitForStartTimeoutSeconds { get; set; } = 300;
    /// <summary>
    /// True if the data folder lives inside the install folder, so the install folder must not be moved
    /// </summary>
    public bool PortableMode { get; set; }
    /// <summary>
    /// True if a graphical privilege prompt can be shown to the user
    /// </summary>
    public bool InteractiveSession { get; set; }
}

/// <summary>
/// The result the helper writes when it finishes
/// </summary>
public sealed class InstallResult
{
    /// <summary>
    /// True if the package was installed and the application restarted
    /// </summary>
    public bool Success { get; set; }
    /// <summary>
    /// The stage that failed, when not successful
    /// </summary>
    public string? Stage { get; set; }
    /// <summary>
    /// The error message, when not successful
    /// </summary>
    public string? Error { get; set; }
    /// <summary>
    /// True if the helper restored the previous installation after a failure
    /// </summary>
    public bool RolledBack { get; set; }
    /// <summary>
    /// The version that was being installed
    /// </summary>
    public string TargetVersion { get; set; } = "";
    /// <summary>
    /// When the helper started
    /// </summary>
    public DateTime StartedUtc { get; set; }
    /// <summary>
    /// When the helper finished
    /// </summary>
    public DateTime FinishedUtc { get; set; }
    /// <summary>
    /// The helper log file
    /// </summary>
    public string? LogFile { get; set; }
    /// <summary>
    /// Paths the new instance should delete once it runs; files and folders the helper could not remove
    /// </summary>
    public string[] CleanupPaths { get; set; } = [];
}

/// <summary>
/// The record written by the server before it exits, so the new instance can complete the install
/// </summary>
public sealed class PendingInstallRecord
{
    /// <summary>
    /// The version being installed
    /// </summary>
    public string TargetVersion { get; set; } = "";
    /// <summary>
    /// The version that started the install
    /// </summary>
    public string PreviousVersion { get; set; } = "";
    /// <summary>
    /// The helper process id
    /// </summary>
    public int HelperPid { get; set; }
    /// <summary>
    /// The pipe the helper listens on for the started signal
    /// </summary>
    public string HelperListenPipe { get; set; } = "";
    /// <summary>
    /// The folder the helper was copied to
    /// </summary>
    public string HelperDirectory { get; set; } = "";
    /// <summary>
    /// The downloaded package
    /// </summary>
    public string PackagePath { get; set; } = "";
    /// <summary>
    /// The install folder, used to decide which cleanup paths are acceptable
    /// </summary>
    public string InstallDir { get; set; } = "";
    /// <summary>
    /// The AppImage path when the package type is appimage, used to decide which cleanup paths are acceptable
    /// </summary>
    public string? AppImagePath { get; set; }
    /// <summary>
    /// The file the helper writes its result to
    /// </summary>
    public string ResultFile { get; set; } = "";
    /// <summary>
    /// The folder where the started marker is written
    /// </summary>
    public string MarkerFolder { get; set; } = "";
    /// <summary>
    /// When the install started
    /// </summary>
    public DateTime StartedUtc { get; set; }
    /// <summary>
    /// The restart kind that was requested
    /// </summary>
    public string RestartKind { get; set; } = "";
}
