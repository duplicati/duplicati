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

extern alias Helper;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;
using JsonSignature;
using Helper::Duplicati.UpdateHelper;
using Helper::Duplicati.UpdateHelper.Installers;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using HelperJob = Helper::Duplicati.Library.AutoUpdater.UpdateJob;
using HelperMessageDto = Helper::Duplicati.Library.AutoUpdater.HelperMessage;
using HelperProtocol = Helper::Duplicati.Library.AutoUpdater.UpdateHelperProtocol;
using HelperRestart = Helper::Duplicati.Library.AutoUpdater.RestartInstruction;
using HelperResult = Helper::Duplicati.Library.AutoUpdater.InstallResult;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the automatic update installation: support detection, host detection,
    /// the pending record, the protocol shared with the helper, and the helper itself.
    /// </summary>
    [TestFixture]
    [Category("UpdateInstall")]
    public class UpdateInstallTests
    {
        private static HostEnvironment MakeHost(SupervisorKind supervisor, bool interactive, string? systemdRun, string? appImage)
            => new HostEnvironment(
                Origin: "Server",
                Supervisor: supervisor,
                ParentPid: supervisor == SupervisorKind.ServiceWrapper ? 4242 : -1,
                ProcessPath: "/opt/duplicati/duplicati-server",
                Arguments: ["--webservice-port=8200", "--ping-pong-keepalive=true"],
                SystemdUnit: supervisor == SupervisorKind.Systemd ? "duplicati.service" : null,
                SystemdUserUnit: false,
                SystemdRunPath: systemdRun,
                LaunchdLabel: "com.duplicati.server.launchagent",
                LaunchdUid: 501,
                WindowsServiceName: supervisor == SupervisorKind.WindowsService ? "Duplicati" : null,
                AppImagePath: appImage,
                InteractiveSession: interactive);

        [Test]
        public void SupportedFormatsAreAccepted()
        {
            var host = MakeHost(SupervisorKind.None, true, null, "/home/me/Duplicati.AppImage");
            foreach (var format in new[] { "zip", "appimage", "msi", "deb", "rpm", "pkg", "dmg" })
            {
                var support = UpdateInstallSupport.Evaluate($"linux-x64-gui.{format}", host, helperExists: true, updatesDisabled: false, isPrivileged: false);
                Assert.IsTrue(support.Supported, $"{format}: {support.Reason}");
                Assert.AreEqual(format, support.PackageFormat);
            }
        }

        [TestCase("linux-x64-gui.docker")]
        [TestCase("linux-arm64-gui.spk")]
        [TestCase("linux-x64-gui.qpkg")]
        [TestCase("")]
        [TestCase("debug")]
        public void UnsupportedPackageTypesAreRefusedWithAReason(string packageTypeId)
        {
            var host = MakeHost(SupervisorKind.None, true, null, null);
            var support = UpdateInstallSupport.Evaluate(packageTypeId, host, helperExists: true, updatesDisabled: false, isPrivileged: true);
            Assert.IsFalse(support.Supported);
            Assert.IsFalse(string.IsNullOrWhiteSpace(support.Reason));
        }

        [Test]
        public void RootFormatsNeedRootOrAnInteractiveSession()
        {
            var headless = MakeHost(SupervisorKind.None, false, null, null);
            var desktop = MakeHost(SupervisorKind.None, true, null, null);

            Assert.IsFalse(UpdateInstallSupport.Evaluate("linux-x64-gui.deb", headless, true, false, false).Supported);
            Assert.IsTrue(UpdateInstallSupport.Evaluate("linux-x64-gui.deb", headless, true, false, true).Supported);
            Assert.IsTrue(UpdateInstallSupport.Evaluate("linux-x64-gui.deb", desktop, true, false, false).Supported);
            Assert.IsTrue(UpdateInstallSupport.Evaluate("linux-x64-gui.zip", headless, true, false, false).Supported);
        }

        [Test]
        public void OtherPreconditionsAreChecked()
        {
            var host = MakeHost(SupervisorKind.None, true, null, null);
            Assert.IsFalse(UpdateInstallSupport.Evaluate("linux-x64-gui.zip", host, helperExists: false, updatesDisabled: false, isPrivileged: true).Supported);
            Assert.IsFalse(UpdateInstallSupport.Evaluate("linux-x64-gui.zip", host, helperExists: true, updatesDisabled: true, isPrivileged: true).Supported);
            Assert.IsFalse(UpdateInstallSupport.Evaluate("linux-x64-gui.appimage", host, true, false, true).Supported, "appimage without a path");

            var systemdWithoutRun = MakeHost(SupervisorKind.Systemd, false, null, null);
            var systemdWithRun = MakeHost(SupervisorKind.Systemd, false, "/usr/bin/systemd-run", null);
            Assert.IsFalse(UpdateInstallSupport.Evaluate("linux-x64-gui.deb", systemdWithoutRun, true, false, true).Supported);
            Assert.IsTrue(UpdateInstallSupport.Evaluate("linux-x64-gui.deb", systemdWithRun, true, false, true).Supported);

            var service = MakeHost(SupervisorKind.WindowsService, false, null, null);
            Assert.IsTrue(UpdateInstallSupport.Evaluate("win-x64-gui.msi", service, true, false, true).Supported);
            Assert.IsFalse(UpdateInstallSupport.Evaluate("win-x64-gui.deb", service, true, false, true).Supported);
        }

        [TestCase("/system.slice/duplicati.service", "duplicati.service", false)]
        [TestCase("/user.slice/user-1000.slice/user@1000.service/app.slice/duplicati.service", "duplicati.service", true)]
        [TestCase("/user.slice/user-1000.slice/session-2.scope", null, false)]
        public void SystemdUnitIsReadFromTheCgroupPath(string path, string? expectedUnit, bool expectedUser)
        {
            var parsed = HostEnvironment.ParseSystemdUnitFromCgroupPath(path);
            if (expectedUnit == null)
            {
                Assert.IsNull(parsed);
                return;
            }

            Assert.IsNotNull(parsed);
            Assert.AreEqual(expectedUnit, parsed!.Value.Unit);
            Assert.AreEqual(expectedUser, parsed.Value.UserUnit);
        }

        [Test]
        public void RestartInstructionFollowsTheSupervisor()
        {
            var standalone = MakeHost(SupervisorKind.None, true, null, null).CreateRestartInstruction();
            Assert.AreEqual(UpdateHelperProtocol.RestartSpawn, standalone.Kind);
            Assert.AreEqual("/opt/duplicati/duplicati-server", standalone.Executable);
            Assert.AreEqual(2, standalone.Arguments.Length);

            var appImage = MakeHost(SupervisorKind.None, true, null, "/home/me/Duplicati.AppImage").CreateRestartInstruction();
            Assert.AreEqual("/home/me/Duplicati.AppImage", appImage.Executable, "the AppImage is spawned, not the extracted binary");

            var systemd = MakeHost(SupervisorKind.Systemd, false, "/usr/bin/systemd-run", null).CreateRestartInstruction();
            Assert.AreEqual(UpdateHelperProtocol.RestartSystemd, systemd.Kind);
            Assert.AreEqual("duplicati.service", systemd.SystemdUnit);

            var launchd = MakeHost(SupervisorKind.Launchd, true, null, null).CreateRestartInstruction();
            Assert.AreEqual(UpdateHelperProtocol.RestartLaunchd, launchd.Kind);
            Assert.AreEqual("com.duplicati.server.launchagent", launchd.LaunchdLabel);
            Assert.AreEqual(501, launchd.LaunchdUid);

            var service = MakeHost(SupervisorKind.WindowsService, false, null, null).CreateRestartInstruction();
            Assert.AreEqual(UpdateHelperProtocol.RestartWindowsService, service.Kind);
            Assert.AreEqual("Duplicati", service.WindowsServiceName);

            var wrapperHost = MakeHost(SupervisorKind.ServiceWrapper, false, null, null);
            var wrapper = wrapperHost.CreateRestartInstruction();
            Assert.AreEqual(UpdateHelperProtocol.RestartSpawn, wrapper.Kind);
            Assert.IsFalse(wrapper.Arguments.Any(x => x.StartsWith("--ping-pong-keepalive")), "the wrapper adds the keepalive option itself");
            CollectionAssert.AreEqual(new[] { Environment.ProcessId, 4242 }, wrapperHost.GetWaitPids());
            CollectionAssert.AreEqual(new[] { 4242 }, wrapperHost.GetTerminatePids());
            Assert.IsTrue(wrapperHost.ServerExitsWhenParentExits);
            Assert.IsFalse(MakeHost(SupervisorKind.Systemd, false, null, null).ServerExitsBySelf);
            Assert.IsTrue(MakeHost(SupervisorKind.None, false, null, null).ServerExitsBySelf);
        }

        [Test]
        public void HostKindIsReadFromTheDeclaringHost()
        {
            var previousKind = Environment.GetEnvironmentVariable(HostEnvironment.HostKindEnvName);
            var previousPid = Environment.GetEnvironmentVariable(HostEnvironment.HostPidEnvName);
            var previousName = Environment.GetEnvironmentVariable(HostEnvironment.HostNameEnvName);
            try
            {
                Environment.SetEnvironmentVariable(HostEnvironment.HostKindEnvName, HostEnvironment.HostKindServiceWrapper);
                Environment.SetEnvironmentVariable(HostEnvironment.HostPidEnvName, "4242");
                Environment.SetEnvironmentVariable(HostEnvironment.HostNameEnvName, null);
                var wrapper = HostEnvironment.Detect("Server", ["--ping-pong-keepalive=true"]);
                Assert.AreEqual(SupervisorKind.ServiceWrapper, wrapper.Supervisor);
                Assert.AreEqual(4242, wrapper.ParentPid);

                Environment.SetEnvironmentVariable(HostEnvironment.HostKindEnvName, HostEnvironment.HostKindWindowsService);
                Environment.SetEnvironmentVariable(HostEnvironment.HostPidEnvName, null);
                Environment.SetEnvironmentVariable(HostEnvironment.HostNameEnvName, "Duplicati.Agent");
                var service = HostEnvironment.Detect("Agent", []);
                Assert.AreEqual(SupervisorKind.WindowsService, service.Supervisor);
                Assert.AreEqual("Duplicati.Agent", service.WindowsServiceName);

                // The ping-pong option alone no longer implies a wrapper
                Environment.SetEnvironmentVariable(HostEnvironment.HostKindEnvName, null);
                Environment.SetEnvironmentVariable(HostEnvironment.HostNameEnvName, null);
                if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVOCATION_ID")))
                    Assert.AreNotEqual(SupervisorKind.ServiceWrapper, HostEnvironment.Detect("Server", ["--ping-pong-keepalive=true"]).Supervisor);
            }
            finally
            {
                Environment.SetEnvironmentVariable(HostEnvironment.HostKindEnvName, previousKind);
                Environment.SetEnvironmentVariable(HostEnvironment.HostPidEnvName, previousPid);
                Environment.SetEnvironmentVariable(HostEnvironment.HostNameEnvName, previousName);
            }
        }

        [Test]
        public void PendingRecordRoundTrips()
        {
            using var tmp = new Library.Utility.TempFolder();
            var updates = PendingInstallStore.GetUpdatesFolder(tmp);
            Assert.IsTrue(Directory.Exists(updates));
            Assert.IsNull(PendingInstallStore.Read(updates));

            var record = new PendingInstallRecord
            {
                TargetVersion = "2.2.0.100",
                PreviousVersion = "2.2.0.99",
                HelperPid = 1234,
                HelperListenPipe = "duplicati-update-helper-1234",
                HelperDirectory = Path.Combine(updates, "helper-x"),
                PackagePath = Path.Combine(updates, "pkg.zip"),
                ResultFile = Path.Combine(updates, "helper-x", "install-result.json"),
                MarkerFolder = updates,
                StartedUtc = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc),
                RestartKind = UpdateHelperProtocol.RestartSpawn,
            };
            PendingInstallStore.Write(updates, record);

            var read = PendingInstallStore.Read(updates);
            Assert.IsNotNull(read);
            Assert.AreEqual(record.TargetVersion, read!.TargetVersion);
            Assert.AreEqual(record.HelperPid, read.HelperPid);
            Assert.AreEqual(record.HelperListenPipe, read.HelperListenPipe);
            Assert.AreEqual(record.StartedUtc, read.StartedUtc);

            PendingInstallStore.Delete(updates);
            Assert.IsNull(PendingInstallStore.Read(updates));
        }

        [Test]
        public void ServerAndHelperAgreeOnTheWireFormat()
        {
            // The server serializes with reflection, the helper with a source generated context
            var serverMessage = new HelperMessage
            {
                Type = UpdateHelperProtocol.TypeJob,
                ProtocolVersion = UpdateHelperProtocol.ProtocolVersion,
                Pid = 77,
                Version = "2.2.0.99",
                Job = new UpdateJob
                {
                    PackagePath = "/tmp/pkg.zip",
                    PackageLength = 42,
                    PackageSha256 = "abc=",
                    PackageTypeId = "linux-x64-gui.zip",
                    SignedManifest = "//SIGJSONv1: aGVhZGVy.c2ln\n{}",
                    InstallDir = "/opt/duplicati",
                    TargetVersion = "2.2.0.100",
                    CurrentVersion = "2.2.0.99",
                    WaitPids = [1, 2],
                    TerminatePids = [3],
                    TerminateProcessNames = ["Duplicati.GUI.TrayIcon"],
                    Restart = new RestartInstruction { Kind = UpdateHelperProtocol.RestartSystemd, SystemdUnit = "duplicati.service" },
                    MsiProperties = new Dictionary<string, string> { ["INSTALLLOCATION"] = @"C:\Program Files\Duplicati 2" },
                    ResultFile = "/tmp/result.json",
                    LogFile = "/tmp/helper.log",
                    MarkerFolder = "/tmp",
                    PortableMode = true,
                    InteractiveSession = true,
                }
            };

            var line = JsonSerializer.Serialize(serverMessage);
            var helperMessage = JsonSerializer.Deserialize(line, Helper::Duplicati.UpdateHelper.HelperJsonContext.Default.HelperMessage);
            Assert.IsNotNull(helperMessage?.Job);
            Assert.AreEqual(HelperProtocol.TypeJob, helperMessage!.Type);
            Assert.AreEqual(42, helperMessage.Job!.PackageLength);
            Assert.AreEqual("linux-x64-gui.zip", helperMessage.Job.PackageTypeId);
            Assert.AreEqual(serverMessage.Job.SignedManifest, helperMessage.Job.SignedManifest);
            CollectionAssert.AreEqual(new[] { 1, 2 }, helperMessage.Job.WaitPids);
            Assert.AreEqual("duplicati.service", helperMessage.Job.Restart.SystemdUnit);
            Assert.AreEqual(@"C:\Program Files\Duplicati 2", helperMessage.Job.MsiProperties["INSTALLLOCATION"]);
            Assert.IsTrue(helperMessage.Job.PortableMode);

            var reply = JsonSerializer.Serialize(new HelperMessageDto { Type = HelperProtocol.TypeAbort, ProtocolVersion = 1, Pid = 5, Reason = "nope" }, Helper::Duplicati.UpdateHelper.HelperJsonContext.Default.HelperMessage);
            var serverReply = JsonSerializer.Deserialize<HelperMessage>(reply);
            Assert.AreEqual(UpdateHelperProtocol.TypeAbort, serverReply!.Type);
            Assert.AreEqual("nope", serverReply.Reason);

            var result = JsonSerializer.Serialize(new HelperResult { Success = false, Stage = "install", Error = "boom", CleanupPaths = ["/tmp/x.old"] }, Helper::Duplicati.UpdateHelper.HelperJsonContext.Default.InstallResult);
            var serverResult = JsonSerializer.Deserialize<InstallResult>(result);
            Assert.IsFalse(serverResult!.Success);
            Assert.AreEqual("install", serverResult.Stage);
            CollectionAssert.AreEqual(new[] { "/tmp/x.old" }, serverResult.CleanupPaths);
        }

        private const string PackageTypeId = "linux-x64-gui.zip";

        /// <summary>
        /// The key that signs test manifests; the install folders carry its public half as the OEM key file
        /// </summary>
        private static readonly RSA TestKey = RSA.Create(2048);
        /// <summary>
        /// A key the helper does not trust
        /// </summary>
        private static readonly RSA OtherKey = RSA.Create(2048);

        /// <summary>
        /// Creates a signed manifest listing the package, in the format the server downloads
        /// </summary>
        private static string CreateSignedManifest(string packagePath, string packageTypeId, string version, RSA key)
        {
            long length;
            string sha256;
            using (var fs = File.OpenRead(packagePath))
            {
                length = fs.Length;
                sha256 = Convert.ToBase64String(SHA256.HashData(fs));
            }

            var info = new UpdateInfo(2, "https://example.com/incompatible", "Test " + version, version, DateTime.UtcNow, "canary", null, null, 2,
                [new PackageEntry(["https://example.com/" + Path.GetFileName(packagePath)], length, "", sha256, packageTypeId)],
                "https://example.com/download");

            using var source = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(info));
            using var target = new MemoryStream();
            JSONSignature.SignAsync(source, target, [new JSONSignature.SignOperation(JSONSignature.RSA_SHA256, key.ToXmlString(false), key.ToXmlString(true))]).Await();
            return Encoding.UTF8.GetString(target.ToArray());
        }

        /// <summary>
        /// Creates an install folder with a package type id and some files
        /// </summary>
        private static string CreateInstallDir(string root, string name, string marker)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "package_type_id.txt"), PackageTypeId);
            File.WriteAllText(Path.Combine(dir, TrustedKeys.OemKeyFilename), TestKey.ToXmlString(false));
            File.WriteAllText(Path.Combine(dir, "duplicati-server"), marker);
            File.WriteAllText(Path.Combine(dir, "sub", "lib.dll"), marker);
            return dir;
        }

        /// <summary>
        /// Creates a zip with one top-level folder, like the release builder does
        /// </summary>
        private static string CreateZipPackage(string root, string packageTypeId, string marker)
        {
            var zipPath = Path.Combine(root, "update.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                const string top = "duplicati-2.2.0.100_canary_2026-10-26-linux-x64-gui/";
                AddEntry(zip, top + "package_type_id.txt", packageTypeId, false);
                AddEntry(zip, top + "duplicati-server", marker, true);
                AddEntry(zip, top + "newfile.txt", marker, false);
                AddEntry(zip, top + "sub/lib.dll", marker, false);
            }
            return zipPath;
        }

        private static void AddEntry(ZipArchive zip, string name, string content, bool executable)
        {
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = Convert.ToInt32(executable ? "755" : "644", 8) << 16;
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }

        private static HelperJob MakeZipJob(string installDir, string zipPath, bool portable)
            => MakeJob(installDir, zipPath, PackageTypeId, portable);

        private static HelperJob MakeJob(string installDir, string packagePath, string packageTypeId, bool portable)
        {
            using var fs = File.OpenRead(packagePath);
            return new HelperJob
            {
                PackagePath = packagePath,
                PackageLength = fs.Length,
                PackageSha256 = Convert.ToBase64String(SHA256.HashData(fs)),
                PackageTypeId = packageTypeId,
                SignedManifest = CreateSignedManifest(packagePath, packageTypeId, "2.2.0.100", TestKey),
                InstallDir = installDir + Path.DirectorySeparatorChar,
                TargetVersion = "2.2.0.100",
                CurrentVersion = "2.2.0.99",
                Restart = new HelperRestart { Kind = HelperProtocol.RestartNone },
                ResultFile = Path.Combine(Path.GetDirectoryName(installDir)!, "install-result.json"),
                LogFile = Path.Combine(Path.GetDirectoryName(installDir)!, "helper.log"),
                MarkerFolder = Path.GetDirectoryName(installDir)!,
                PortableMode = portable,
                WaitForExitTimeoutSeconds = 5,
                WaitForStartTimeoutSeconds = 5,
            };
        }

        [Test]
        public void ZipInstallerSwapsTheInstallFolder()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);

            using var log = new Helper::Duplicati.UpdateHelper.HelperLog();
            var installer = new ZipInstaller(log);
            Assert.IsNull(installer.Prepare(job));
            installer.Install(job);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "sub", "lib.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(installDir, "newfile.txt")));
            Assert.IsFalse(Directory.GetDirectories(tmp).Any(x => Path.GetFileName(x).StartsWith(".duplicati.update-")), "staging folder is removed");
            var oldDir = Directory.GetDirectories(tmp).SingleOrDefault(x => Path.GetFileName(x).StartsWith("duplicati.old-"));
            Assert.IsNotNull(oldDir, "the old folder is kept until the new instance runs");
            CollectionAssert.AreEqual(new[] { oldDir }, installer.CleanupPaths, "the new instance deletes the old folder");
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(oldDir!, "duplicati-server")));

            if (!OperatingSystem.IsWindows())
                Assert.IsTrue(File.GetUnixFileMode(Path.Combine(installDir, "duplicati-server")).HasFlag(UnixFileMode.UserExecute), "execute bit restored from the zip");
        }

        [Test]
        public void ZipInstallerRefusesAPackageForAnotherInstallation()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, "win-x64-gui.zip", "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);

            using var log = new Helper::Duplicati.UpdateHelper.HelperLog();
            var problem = new ZipInstaller(log).Prepare(job);
            Assert.IsNotNull(problem);
            StringAssert.Contains("win-x64-gui.zip", problem ?? "");
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(installDir, "duplicati-server")), "nothing changed");
        }

        [Test]
        public void ZipInstallerKeepsTheDataFolderInPortableMode()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            Directory.CreateDirectory(Path.Combine(installDir, "data"));
            File.WriteAllText(Path.Combine(installDir, "data", "Duplicati-server.sqlite"), "precious");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: true);

            using var log = new Helper::Duplicati.UpdateHelper.HelperLog();
            var installer = new ZipInstaller(log);
            Assert.IsNull(installer.Prepare(job));
            installer.Install(job);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));
            Assert.AreEqual("precious", File.ReadAllText(Path.Combine(installDir, "data", "Duplicati-server.sqlite")));
            Assert.IsFalse(installer.CanRollback, "files were copied in place, there is nothing to swap back");
        }

        [Test]
        public void ZipInstallerRollsBackAfterASwap()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);

            using var log = new Helper::Duplicati.UpdateHelper.HelperLog();
            var installer = new ZipInstaller(log);
            Assert.IsNull(installer.Prepare(job));
            installer.Install(job);
            Assert.IsTrue(installer.CanRollback);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));

            installer.Rollback(job);

            Assert.AreEqual("old", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));
            Assert.IsFalse(File.Exists(Path.Combine(installDir, "newfile.txt")));
            Assert.IsFalse(installer.CanRollback);
            var failedDir = Directory.GetDirectories(tmp).SingleOrDefault(x => Path.GetFileName(x).StartsWith("duplicati.failed-"));
            Assert.IsNotNull(failedDir, "the failed install is kept for inspection and listed for cleanup");
            CollectionAssert.AreEqual(new[] { failedDir }, installer.CleanupPaths);
        }

        [Test]
        public void FindPackageRootHandlesBothLayouts()
        {
            using var tmp = new Library.Utility.TempFolder();
            var flat = Path.Combine(tmp, "flat");
            Directory.CreateDirectory(flat);
            File.WriteAllText(Path.Combine(flat, "package_type_id.txt"), PackageTypeId);
            Assert.AreEqual(flat, ZipInstaller.FindPackageRoot(flat));

            var nested = Path.Combine(tmp, "nested");
            var inner = Path.Combine(nested, "duplicati-x");
            Directory.CreateDirectory(inner);
            File.WriteAllText(Path.Combine(inner, "package_type_id.txt"), PackageTypeId);
            Assert.AreEqual(inner, ZipInstaller.FindPackageRoot(nested));

            var empty = Path.Combine(tmp, "empty");
            Directory.CreateDirectory(empty);
            Assert.Throws<InvalidOperationException>(() => ZipInstaller.FindPackageRoot(empty));
        }

        /// <summary>
        /// Plays the server side of the handshake against the helper session running in-process,
        /// then lets the helper perform a zip install with nothing to wait for and nothing to restart.
        /// </summary>
        [Test]
        public void HelperHandshakeAndInstallEndToEnd()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);

            var pipeName = "dup-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            const string secret = "s3cret";

            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var log = new Helper::Duplicati.UpdateHelper.HelperLog();
            var session = new Helper::Duplicati.UpdateHelper.HelperSession(log);

            var helperTask = Task.Run(() => session.Handshake(pipeName, secret));

            pipe.WaitForConnectionAsync().Wait(TimeSpan.FromSeconds(30));
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            var hello = JsonSerializer.Deserialize<HelperMessage>(reader.ReadLine()!)!;
            Assert.AreEqual(UpdateHelperProtocol.TypeHello, hello.Type);
            Assert.AreEqual(secret, hello.Secret);
            Assert.AreEqual(Environment.ProcessId, hello.Pid);

            // Hand over the job as the server would: serialized with the server-side types
            var serverJob = JsonSerializer.Deserialize<UpdateJob>(JsonSerializer.Serialize(job, Helper::Duplicati.UpdateHelper.HelperJsonContext.Default.UpdateJob))!;
            writer.WriteLine(JsonSerializer.Serialize(new HelperMessage { Type = UpdateHelperProtocol.TypeJob, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId, Job = serverJob }));

            var ready = JsonSerializer.Deserialize<HelperMessage>(reader.ReadLine()!)!;
            Assert.AreEqual(UpdateHelperProtocol.TypeReady, ready.Type, ready.Reason ?? "");

            writer.WriteLine(JsonSerializer.Serialize(new HelperMessage { Type = UpdateHelperProtocol.TypeGo, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId }));

            Assert.IsTrue(helperTask.Wait(TimeSpan.FromSeconds(30)));
            var handshake = helperTask.Result;
            Assert.IsNotNull(handshake, "the helper should have accepted the job");

            var ok = session.Perform(handshake!.Value.Job, handshake.Value.Installer, DateTime.UtcNow);
            Assert.IsTrue(ok);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));
            var result = PendingInstallStore.ReadResult(job.ResultFile);
            Assert.IsNotNull(result);
            Assert.IsTrue(result!.Success, result.Error ?? "");
            Assert.AreEqual("2.2.0.100", result.TargetVersion);
        }

        /// <summary>
        /// Drives a published helper executable through the real launcher: stdin secret, pipe handshake,
        /// pending record, zip install, result file. Needs the helper published as a single file, pointed
        /// to by the DUPLICATI_UPDATE_HELPER_EXE environment variable; skipped otherwise.
        /// </summary>
        [Test]
        public void PublishedHelperProcessEndToEnd()
        {
            var helperExe = Environment.GetEnvironmentVariable("DUPLICATI_UPDATE_HELPER_EXE");
            if (string.IsNullOrWhiteSpace(helperExe) || !File.Exists(helperExe))
                Assert.Ignore("Set DUPLICATI_UPDATE_HELPER_EXE to a published update helper to run this test");

            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var updatesFolder = PendingInstallStore.GetUpdatesFolder(tmp);
            Directory.CreateDirectory(updatesFolder);
            // The package lives in the updates folder, where the server downloads it; completion only deletes it there
            var zipPath = CreateZipPackage(updatesFolder, PackageTypeId, "new");
            var host = MakeHost(SupervisorKind.None, true, null, null) with { ProcessPath = "" };

            long length;
            string sha256;
            using (var fs = File.OpenRead(zipPath))
            {
                length = fs.Length;
                sha256 = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(fs));
            }

            var messages = new List<string>();
            var request = new UpdateLaunchRequest(
                PackagePath: zipPath,
                PackageLength: length,
                PackageSha256: sha256,
                PackageTypeId: PackageTypeId,
                SignedManifest: CreateSignedManifest(zipPath, PackageTypeId, "2.2.0.100", TestKey),
                TargetVersion: "2.2.0.100",
                CurrentVersion: "2.2.0.99",
                Host: host,
                InstallDir: installDir,
                HelperSourcePath: helperExe,
                WaitPids: [],
                TerminatePids: [],
                UpdatesFolder: updatesFolder,
                MsiProperties: new Dictionary<string, string>(),
                PortableMode: false,
                FinalCheck: () => null);

            var launched = UpdateHelperLauncher.LaunchAsync(request, messages.Add, CancellationToken.None).Await();
            Assert.Greater(launched.HelperPid, 0);
            Assert.IsNotNull(PendingInstallStore.Read(updatesFolder));

            // Nothing to wait for and nothing to restart, so the helper installs and exits on its own
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline && !File.Exists(launched.Record.ResultFile))
                Thread.Sleep(200);

            var result = PendingInstallStore.ReadResult(launched.Record.ResultFile);
            Assert.IsNotNull(result, "the helper wrote no result; messages: " + string.Join("; ", messages));
            Assert.IsTrue(result!.Success, result.Error ?? "");
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installDir, "duplicati-server")));

            var completed = UpdateInstallCompletion.CompleteAsync(updatesFolder, launched.Record, "2.2.0.100", messages.Add, CancellationToken.None).Await();
            Assert.IsTrue(completed.Success, completed.Error ?? "");
            Assert.IsNull(PendingInstallStore.Read(updatesFolder), "the record is removed after completion");
            Assert.IsFalse(Directory.Exists(launched.Record.HelperDirectory), "the helper folder is removed after a successful install");
            Assert.IsFalse(File.Exists(zipPath), "the package is removed after completion");
            Assert.IsFalse(Directory.GetDirectories(tmp).Any(x => Path.GetFileName(x).StartsWith("duplicati.old-")), "the old folder is removed through the cleanup list");
        }

        /// <summary>
        /// Drives the handshake with a job the helper should refuse and returns the abort reason
        /// </summary>
        private static string AbortReasonFor(HelperJob job)
        {
            var pipeName = "dup-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var log = new HelperLog();
            var session = new HelperSession(log);
            var helperTask = Task.Run(() => session.Handshake(pipeName, "x"));

            pipe.WaitForConnectionAsync().Wait(TimeSpan.FromSeconds(30));
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            reader.ReadLine();

            var serverJob = JsonSerializer.Deserialize<UpdateJob>(JsonSerializer.Serialize(job, HelperJsonContext.Default.UpdateJob))!;
            writer.WriteLine(JsonSerializer.Serialize(new HelperMessage { Type = UpdateHelperProtocol.TypeJob, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId, Job = serverJob }));

            var reply = JsonSerializer.Deserialize<HelperMessage>(reader.ReadLine()!)!;
            Assert.AreEqual(UpdateHelperProtocol.TypeAbort, reply.Type, reply.Reason ?? "");
            Assert.IsTrue(helperTask.Wait(TimeSpan.FromSeconds(30)));
            Assert.IsNull(helperTask.Result);
            return reply.Reason ?? "";
        }

        [Test]
        public void HelperAbortsWhenTheJobHashDiffersFromTheManifest()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);
            job.PackageSha256 = Convert.ToBase64String(new byte[32]);

            StringAssert.Contains("signed manifest", AbortReasonFor(job));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(installDir, "duplicati-server")), "nothing changed");
        }

        [Test]
        public void HelperAbortsWhenThePackageChangedAfterSigning()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);

            // Flip the last byte, keeping the length
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Seek(-1, SeekOrigin.End);
                var b = fs.ReadByte();
                fs.Seek(-1, SeekOrigin.End);
                fs.WriteByte((byte)(b ^ 0xff));
            }

            StringAssert.Contains("SHA256", AbortReasonFor(job));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(installDir, "duplicati-server")), "nothing changed");
        }

        [Test]
        public void HelperAbortsWhenTheManifestIsSignedByAnUnknownKey()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);
            job.SignedManifest = CreateSignedManifest(zipPath, PackageTypeId, "2.2.0.100", OtherKey);

            StringAssert.Contains("trusted key", AbortReasonFor(job));
        }

        [Test]
        public void HelperAbortsWhenTheManifestIsForAnotherVersion()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);
            job.SignedManifest = CreateSignedManifest(zipPath, PackageTypeId, "2.2.0.101", TestKey);

            StringAssert.Contains("version", AbortReasonFor(job));
        }

        [Test]
        public void HelperAbortsWithoutASignedManifest()
        {
            using var tmp = new Library.Utility.TempFolder();
            var installDir = CreateInstallDir(tmp, "duplicati", "old");
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var job = MakeZipJob(installDir, zipPath, portable: false);
            job.SignedManifest = "";

            StringAssert.Contains("manifest", AbortReasonFor(job));
        }

        [Test]
        public void SignedManifestVerifierAgreesWithTheServerLibrary()
        {
            using var tmp = new Library.Utility.TempFolder();
            var zipPath = CreateZipPackage(tmp, PackageTypeId, "new");
            var text = CreateSignedManifest(zipPath, PackageTypeId, "2.2.0.100", TestKey);
            var keys = TrustedKeys.Parse(TestKey.ToXmlString(false));

            var info = SignedManifest.Verify(text, keys);
            Assert.AreEqual("2.2.0.100", info.Version);
            Assert.AreEqual(PackageTypeId, info.Packages![0].PackageTypeId);
            Assert.AreEqual(new FileInfo(zipPath).Length, info.Packages[0].Length);

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                Assert.IsTrue(JSONSignature.VerifyAtLeastOne(ms, [new JSONSignature.VerifyOperation(JSONSignature.RSA_SHA256, TestKey.ToXmlString(false))]), "the server library verifies the same text");

            var tampered = text.Replace("2.2.0.100", "2.2.0.101");
            NUnit.Framework.Assert.Throws<CryptographicException>(() => SignedManifest.Verify(tampered, keys));
            NUnit.Framework.Assert.Throws<CryptographicException>(() => SignedManifest.Verify(text, TrustedKeys.Parse(OtherKey.ToXmlString(false))));
            NUnit.Framework.Assert.Throws<CryptographicException>(() => SignedManifest.Verify("{\"Version\":\"2.2.0.100\"}", keys));

            // Two keys can sign; one trusted key is enough
            using var bothSource = new MemoryStream(Encoding.UTF8.GetBytes("{\"Version\":\"9\",\"Packages\":[]}"));
            using var bothTarget = new MemoryStream();
            JSONSignature.SignAsync(bothSource, bothTarget, [
                new JSONSignature.SignOperation(JSONSignature.RSA_SHA256, OtherKey.ToXmlString(false), OtherKey.ToXmlString(true)),
                new JSONSignature.SignOperation(JSONSignature.RSA_SHA256, TestKey.ToXmlString(false), TestKey.ToXmlString(true))]).Await();
            Assert.AreEqual("9", SignedManifest.Verify(Encoding.UTF8.GetString(bothTarget.ToArray()), keys).Version);
        }

        [Test]
        public void HelperCarriesTheBuiltInSigningKeys()
        {
            using var tmp = new Library.Utility.TempFolder();
            using var log = new HelperLog();

            var builtIn = TrustedKeys.Load(log, tmp);
            Assert.IsNotEmpty(builtIn, "the embedded key resource is present in the helper");

            var serverKeys = AutoUpdateSettings.SignKeys.Select(k => RsaPublicKey.ParseXml(k.ToXmlString(false))!).ToList();
            Assert.IsNotEmpty(serverKeys);
            foreach (var k in serverKeys)
                Assert.IsTrue(builtIn.Any(b => b.Matches(k)), "the helper trusts every key the server trusts");

            // An OEM key file in the install folder replaces the built-in keys
            File.WriteAllText(Path.Combine(tmp, TrustedKeys.OemKeyFilename), TestKey.ToXmlString(false));
            var oem = TrustedKeys.Load(log, tmp);
            Assert.AreEqual(1, oem.Count);
            Assert.IsTrue(oem[0].Matches(RsaPublicKey.ParseXml(TestKey.ToXmlString(false))!));
        }

        [Test]
        public void CleanupOnlyDeletesPathsNearTheInstallation()
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dup-cleanup-test"));
            var updates = Path.Combine(root, "data", "updates");
            var record = new PendingInstallRecord
            {
                InstallDir = Path.Combine(root, "opt", "duplicati") + Path.DirectorySeparatorChar,
                AppImagePath = Path.Combine(root, "apps", "Duplicati.AppImage"),
            };

            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt", "duplicati.old-abcd1234"), record, updates));
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt", "duplicati.failed-abcd1234"), record, updates));
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt", "duplicati", "Duplicati.dll.old-abcd1234"), record, updates));
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "apps", "Duplicati.AppImage.old-abcd1234"), record, updates));
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(updates, "helper-abcd1234"), record, updates));

            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt", "duplicati"), record, updates), "the install folder itself");
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt"), record, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "opt", "other.old-abcd1234"), record, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "apps", "Duplicati.AppImage"), record, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "data"), record, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "home", "user", ".ssh"), record, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath("", record, updates));

            // A macOS bundle: the install folder is Name.app/Contents/MacOS and the leftover is next to the bundle
            var bundleRecord = new PendingInstallRecord { InstallDir = Path.Combine(root, "Applications", "Duplicati.app", "Contents", "MacOS") + Path.DirectorySeparatorChar };
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "Applications", "Duplicati.app.old-abcd1234"), bundleRecord, updates));
            Assert.IsTrue(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "Applications", "Duplicati.app.failed-abcd1234"), bundleRecord, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "Applications", "Other.app.old-abcd1234"), bundleRecord, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "Applications.old-abcd1234"), bundleRecord, updates));
            Assert.IsFalse(UpdateInstallCompletion.IsAllowedCleanupPath(Path.Combine(root, "Applications", "Duplicati.app"), bundleRecord, updates));
        }

        private static void SendStarted(string pipeName, int pid)
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(10000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            writer.WriteLine(JsonSerializer.Serialize(new HelperMessage { Type = UpdateHelperProtocol.TypeStarted, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = pid, Version = "x" }));
        }

        [Test]
        public void StartedReportsFromUnrelatedProcessesAreIgnored()
        {
            using var tmp = new Library.Utility.TempFolder();
            using var log = new HelperLog();
            using var listener = new StartedListener(log, tmp);
            listener.AcceptPid = pid => pid == 4242;

            SendStarted(listener.PipeName, 1);
            Assert.IsFalse(listener.Wait(TimeSpan.FromSeconds(2), () => false), "an unrelated report over the pipe is ignored");

            var marker = Path.Combine(tmp, UpdateHelperProtocol.StartedMarkerPrefix + Environment.ProcessId + ".json");
            File.WriteAllText(marker, JsonSerializer.Serialize(new HelperMessage { Type = UpdateHelperProtocol.TypeStarted, Pid = 7 }));
            Assert.IsFalse(listener.Wait(TimeSpan.FromSeconds(2), () => false), "an unrelated marker is ignored");
            Assert.IsFalse(File.Exists(marker), "the rejected marker is removed");

            SendStarted(listener.PipeName, 4242);
            Assert.IsTrue(listener.Wait(TimeSpan.FromSeconds(10), () => false), "the listener still accepts the related report");
        }

        [Test]
        public void ParentProcessIdsAreResolvedForAnyPid()
        {
            var parent = ProcessInfo.GetParentProcessId();
            Assert.Greater(parent, 0);
            Assert.AreEqual(parent, ProcessInfo.GetParentProcessId(Environment.ProcessId));

            Assert.AreEqual(true, ProcessControl.IsDescendantOf(Environment.ProcessId, Environment.ProcessId));
            Assert.AreEqual(true, ProcessControl.IsDescendantOf(Environment.ProcessId, parent));
            // The parent is not below this process; on Windows the tree may not resolve, which reads as null
            Assert.AreNotEqual(true, ProcessControl.IsDescendantOf(parent, Environment.ProcessId));
        }

        private const string DmgPackageTypeId = "osx-arm64-gui.dmg";

        /// <summary>
        /// Creates a minimal application bundle with a package type id and a marker file
        /// </summary>
        private static string CreateAppBundle(string root, string name, string marker, string packageTypeId)
        {
            var app = Path.Combine(root, name);
            var macos = Path.Combine(app, "Contents", "MacOS");
            Directory.CreateDirectory(macos);
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), "<plist version=\"1.0\"><dict/></plist>");
            File.WriteAllText(Path.Combine(macos, "package_type_id.txt"), packageTypeId);
            File.WriteAllText(Path.Combine(macos, "duplicati"), marker);
            File.WriteAllText(Path.Combine(macos, TrustedKeys.OemKeyFilename), TestKey.ToXmlString(false));
            return app;
        }

        /// <summary>
        /// Creates a disk image holding an application bundle, like the release builder does
        /// </summary>
        private static string CreateDmg(string root, string marker, string packageTypeId)
        {
            var src = Path.Combine(root, "dmg-src");
            Directory.CreateDirectory(src);
            CreateAppBundle(src, "Duplicati.app", marker, packageTypeId);

            var dmg = Path.Combine(root, "update.dmg");
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/hdiutil") { UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in new[] { "create", "-quiet", "-srcfolder", src, "-volname", "Duplicati Test", "-format", "UDZO", dmg })
                psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.AreEqual(0, p.ExitCode, "hdiutil create failed: " + err);
            return dmg;
        }

        [Test]
        public void BundleRootIsFoundFromTheInstallFolder()
        {
            using var tmp = new Library.Utility.TempFolder();
            var app = CreateAppBundle(tmp, "Duplicati.app", "x", DmgPackageTypeId);
            var macos = Path.Combine(app, "Contents", "MacOS") + Path.DirectorySeparatorChar;

            Assert.AreEqual(app, DmgInstaller.FindBundleRoot(macos));
            Assert.AreEqual(app, DmgInstaller.FindBundleRoot(app));
            Assert.IsNull(DmgInstaller.FindBundleRoot(tmp));

            var notABundle = Path.Combine(tmp, "Other.app", "Contents", "MacOS");
            Directory.CreateDirectory(notABundle);
            Assert.IsNull(DmgInstaller.FindBundleRoot(notABundle), "a folder named .app without an Info.plist is not a bundle");
        }

        [Test]
        public void DmgInstallerSwapsTheBundleAndRollsBack()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("Disk images need hdiutil");

            using var tmp = new Library.Utility.TempFolder();
            var apps = Path.Combine(tmp, "apps");
            Directory.CreateDirectory(apps);
            var app = CreateAppBundle(apps, "Duplicati.app", "old", DmgPackageTypeId);
            var dmg = CreateDmg(tmp, "new", DmgPackageTypeId);
            var job = MakeJob(Path.Combine(app, "Contents", "MacOS"), dmg, DmgPackageTypeId, portable: false);

            using var log = new HelperLog();
            var installer = new DmgInstaller(log);
            Assert.IsNull(installer.Prepare(job));
            installer.Install(job);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "duplicati")));
            Assert.AreEqual(DmgPackageTypeId, File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "package_type_id.txt")));
            var old = Directory.GetDirectories(apps).Single(d => Path.GetFileName(d).StartsWith("Duplicati.app.old-"));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(old, "Contents", "MacOS", "duplicati")));
            CollectionAssert.Contains(installer.CleanupPaths, old);
            Assert.IsFalse(Directory.GetDirectories(apps).Any(d => Path.GetFileName(d).Contains(".update-")), "no staging folder is left");
            Assert.IsTrue(installer.CanRollback);

            installer.Rollback(job);
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "duplicati")));
            Assert.IsFalse(Directory.Exists(old));
            var failed = Directory.GetDirectories(apps).Single(d => Path.GetFileName(d).StartsWith("Duplicati.app.failed-"));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(failed, "Contents", "MacOS", "duplicati")));
            CollectionAssert.Contains(installer.CleanupPaths, failed);
            CollectionAssert.DoesNotContain(installer.CleanupPaths, old);
        }

        [Test]
        public void DmgInstallerRefusesAnImageForAnotherInstallation()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("Disk images need hdiutil");

            using var tmp = new Library.Utility.TempFolder();
            var apps = Path.Combine(tmp, "apps");
            Directory.CreateDirectory(apps);
            var app = CreateAppBundle(apps, "Duplicati.app", "old", DmgPackageTypeId);
            var dmg = CreateDmg(tmp, "new", "osx-x64-gui.dmg");
            var job = MakeJob(Path.Combine(app, "Contents", "MacOS"), dmg, DmgPackageTypeId, portable: false);

            using var log = new HelperLog();
            var problem = new DmgInstaller(log).Prepare(job);
            StringAssert.Contains("osx-x64-gui.dmg", problem ?? "");
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "duplicati")));

            var notInBundle = MakeJob(apps, dmg, DmgPackageTypeId, portable: false);
            StringAssert.Contains("application bundle", new DmgInstaller(log).Prepare(notInBundle) ?? "");
        }
    }
}
