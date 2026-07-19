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
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the data folder permission-hardening logic in
    /// <see cref="DataFolderManager"/> and the underlying
    /// <see cref="ISystemIO"/> permission primitives.
    /// </summary>
    [TestFixture]
    public class DataFolderSecurityTests
    {
        /// <summary>
        /// A temporary directory that is created for each test and removed afterwards.
        /// </summary>
        private string m_tempDir = null!;

        /// <summary>
        /// The saved value of the allow-insecure-datafolder environment variable, restored after each test.
        /// </summary>
        private string? m_savedEnv;

        [SetUp]
        public void SetUp()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "dupsec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);

            // Capture and clear the opt-out env var so tests start from a known state.
            m_savedEnv = Environment.GetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar);
            Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, m_savedEnv);

            try
            {
                if (Directory.Exists(m_tempDir))
                {
                    // Ensure the directory is writable/removable even if a test locked it down.
                    if (!OperatingSystem.IsWindows())
                        MakeInsecure(m_tempDir);
                    Directory.Delete(m_tempDir, true);
                }
            }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Makes a directory non-canonical (accessible to group/other on POSIX). On Windows a
        /// freshly created directory already inherits its parent ACL and is therefore not in the
        /// protected canonical form, so no action is required there.
        /// </summary>
        [UnsupportedOSPlatform("windows")]
        private static void MakeInsecure(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        /// <summary>
        /// Returns the effective user id of the current process on POSIX, or 0 on Windows
        /// (where the concept does not apply; callers gate on <see cref="OperatingSystem.IsWindows"/>).
        /// </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc")]
        private static extern uint geteuid();

        /// <summary>
        /// Returns <c>true</c> if the current process is privileged with respect to the
        /// for-service lockdown: root on POSIX, or a member of the Administrators group on Windows.
        /// On POSIX the for-service set step chowns to root and therefore requires root, so tests
        /// that exercise the set step skip when this returns false on POSIX. On Windows the
        /// Administrators principal is one of the trusted SIDs the for-service lockdown grants
        /// access to, so a member of Administrators is effectively the service principal.
        /// </summary>
        private static bool IsCurrentUserPrivilegedForService()
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }

            return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? geteuid() == 0
                : false;
        }

        /// <summary>
        /// Creates a child directory inside the temp dir and returns its path.
        /// </summary>
        private string NewChild(string name)
            => Path.Combine(m_tempDir, name);

        [Test]
        [Category("DataFolderSecurity")]
        public void LockDownThenVerifyRoundTrips()
        {
            var dir = NewChild("roundtrip");
            Directory.CreateDirectory(dir);

            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, false);

            Assert.IsTrue(SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out var detail),
                $"A locked-down folder should pass the canonical permission check, but: {detail}");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void LockDownThenVerifyRoundTrips_ForService()
        {
            var dir = NewChild("roundtrip-service");
            Directory.CreateDirectory(dir);

            // The "for service" mode excludes the current user as a trusted principal and
            // locks the folder down to root (POSIX) or SYSTEM/Administrators (Windows).
            // On POSIX this requires running as root to be able to chown to root, so skip
            // the set step there when not root; verification of an already-root-owned
            // folder is covered by the canonical check on the service path elsewhere.
            if (!OperatingSystem.IsWindows() && !IsCurrentUserPrivilegedForService())
                Assert.Ignore("The for-service lockdown on POSIX requires running as root");

            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, true);

            Assert.IsTrue(SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, true, out var detail),
                $"A folder locked down for service should pass the canonical permission check, but: {detail}");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void InsecureFolderFailsVerification()
        {
            var dir = NewChild("insecure");
            Directory.CreateDirectory(dir);

            if (!OperatingSystem.IsWindows())
                MakeInsecure(dir);

            // On Windows, a freshly created dir inherits its parent ACL (not protected) and so is
            // not canonical; on POSIX we made it 0777 above. Either way it must fail.
            Assert.IsFalse(SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out _),
                "A non-canonical folder must not pass the canonical permission check");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void ForServiceLockdown_AcceptsForServiceCheck()
        {
            var dir = NewChild("service-accepted");
            Directory.CreateDirectory(dir);

            if (!OperatingSystem.IsWindows() && !IsCurrentUserPrivilegedForService())
                Assert.Ignore("The for-service lockdown on POSIX requires running as root");

            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, true);

            // The folder locked down for service must pass the for-service verification.
            Assert.IsTrue(SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, true, out var detail),
                $"A folder locked down for service should pass the for-service check, but: {detail}");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void ForServiceLockdown_RejectsNonServiceCheck()
        {
            var dir = NewChild("service-rejects-non-service");
            Directory.CreateDirectory(dir);

            if (!OperatingSystem.IsWindows() && !IsCurrentUserPrivilegedForService())
                Assert.Ignore("The for-service lockdown on POSIX requires running as root");

            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, true);

            // A folder locked down for service excludes the current user as a trusted principal.
            // How the non-service check treats it differs by platform:
            //
            // - Windows: the for-service lockdown grants the Administrators *group* SID, never
            //   the current user's individual SID. The non-service check requires an explicit
            //   rule for the current user's individual SID, which is distinct from the group SID
            //   even when the current user is a member of Administrators. So the non-service
            //   check always rejects a for-service folder here.
            //
            // - POSIX: the for-service lockdown chowns to root, and root ownership is always
            //   trusted by the non-service check (an unprivileged attacker cannot own a folder
            //   as root). So the non-service check always accepts a for-service folder here.
            //   This test only runs as root on POSIX (it is ignored otherwise, above).
            var result = SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out _);
            if (OperatingSystem.IsWindows())
                Assert.IsFalse(result,
                    "On Windows a folder locked down for service must not pass the non-service check, " +
                    "because it grants the Administrators group SID rather than the current user's individual SID");
            else
                Assert.IsTrue(result,
                    "On POSIX a folder locked down for service is root-owned, which the non-service check always trusts");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void PrepareSecureDataFolder_NewFolder_IsCreatedAndLockedDown()
        {
            var dir = NewChild("created");
            Assert.IsFalse(Directory.Exists(dir), "Precondition: folder should not exist yet");

            DataFolderManager.PrepareSecureDataFolder(dir, createIfMissing: true);

            Assert.IsTrue(Directory.Exists(dir), "The folder should have been created");
            Assert.IsTrue(SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out var detail),
                $"A created folder should be locked down, but: {detail}");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void PrepareSecureDataFolder_MissingFolderWithoutCreate_IsNoOp()
        {
            var dir = NewChild("missing-nocreate");

            // Should neither throw nor create the folder.
            Assert.DoesNotThrow(() => DataFolderManager.PrepareSecureDataFolder(dir, createIfMissing: false));
            Assert.IsFalse(Directory.Exists(dir), "The folder must not be created when createIfMissing is false");
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void PrepareSecureDataFolder_ExistingCanonicalFolder_IsAccepted()
        {
            var dir = NewChild("existing-ok");
            Directory.CreateDirectory(dir);
            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, false);

            // A pre-existing, already-canonical folder must be accepted without modification or error.
            Assert.DoesNotThrow(() => DataFolderManager.PrepareSecureDataFolder(dir, createIfMissing: true));
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void PrepareSecureDataFolder_ExistingInsecureFolder_IsRejected()
        {
            var dir = NewChild("existing-insecure");
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows())
                MakeInsecure(dir);

            // Sanity: the folder must actually be non-canonical for this test to be meaningful.
            if (SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out _))
                Assert.Ignore("Could not produce a non-canonical folder on this platform/filesystem");

            var ex = Assert.Throws<UserInformationException>(
                () => DataFolderManager.PrepareSecureDataFolder(dir, createIfMissing: true),
                "A pre-existing folder with insecure permissions must be rejected");
            Assert.AreEqual("InsecureDataFolderPermissions", ex!.HelpID);
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void PrepareSecureDataFolder_ExistingInsecureFolder_IsAcceptedWhenOptedIn()
        {
            var dir = NewChild("existing-insecure-optin");
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows())
                MakeInsecure(dir);

            if (SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out _))
                Assert.Ignore("Could not produce a non-canonical folder on this platform/filesystem");

            Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, "true");

            // With the opt-out set, an insecure folder is accepted and left untouched.
            Assert.DoesNotThrow(() => DataFolderManager.PrepareSecureDataFolder(dir, createIfMissing: true));
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void VerifyReadOnly_InsecureFolder_IsRejected()
        {
            var dir = NewChild("readonly-insecure");
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows())
                MakeInsecure(dir);

            if (SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dir, false, out _))
                Assert.Ignore("Could not produce a non-canonical folder on this platform/filesystem");

            var ex = Assert.Throws<UserInformationException>(
                () => DataFolderManager.VerifyDataFolderSecurityReadOnly(dir),
                "Read-only verification must reject a non-canonical folder");
            Assert.AreEqual("InsecureDataFolderPermissions", ex!.HelpID);
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void VerifyReadOnly_InsecureFolder_IsAcceptedWhenOptedIn()
        {
            var dir = NewChild("readonly-insecure-optin");
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows())
                MakeInsecure(dir);

            Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, "true");

            Assert.DoesNotThrow(() => DataFolderManager.VerifyDataFolderSecurityReadOnly(dir));
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void VerifyReadOnly_CanonicalFolder_IsAccepted()
        {
            var dir = NewChild("readonly-ok");
            Directory.CreateDirectory(dir);
            SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dir, false);

            Assert.DoesNotThrow(() => DataFolderManager.VerifyDataFolderSecurityReadOnly(dir));
        }

        [Test]
        [Category("DataFolderSecurity")]
        public void AllowInsecureDataFolder_ReadsEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, null);
            Assert.IsFalse(Util.AllowInsecureDataFolder(), "Unset env var should mean not allowed");

            foreach (var truthy in new[] { "true", "1", "yes", "on", "" })
            {
                Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, truthy);
                Assert.IsTrue(Util.AllowInsecureDataFolder(), $"Value '{truthy}' should enable the opt-out");
            }

            foreach (var falsy in new[] { "false", "0", "no", "off" })
            {
                Environment.SetEnvironmentVariable(Util.AllowInsecureDatafolderEnvVar, falsy);
                Assert.IsFalse(Util.AllowInsecureDataFolder(), $"Value '{falsy}' should disable the opt-out");
            }
        }
    }
}
