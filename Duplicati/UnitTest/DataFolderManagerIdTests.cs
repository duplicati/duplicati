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
using System.Reflection;
using Duplicati.Library.AutoUpdater;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the machine-id and install-id handling in <see cref="DataFolderManager"/>.
    /// Verifies that an id read before the data folder has been initialized is not cached,
    /// so a read after initialization picks up the real value.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class DataFolderManagerIdTests
    {
        /// <summary>
        /// A temporary directory that is created for each test and removed afterwards.
        /// </summary>
        private string m_tempDir = null!;

        /// <summary>
        /// Saved environment variable values, restored after each test.
        /// </summary>
        private string? m_savedHomeEnv;
        private string? m_savedPortableEnv;
        private string? m_savedAllowInsecureEnv;

        /// <summary>
        /// The cached id fields in <see cref="DataFolderManager"/>, reset via reflection
        /// to isolate the test from other tests running in the same process.
        /// </summary>
        private static readonly FieldInfo m_machineIdField = typeof(DataFolderManager).GetField("_machineID", BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly FieldInfo m_installIdField = typeof(DataFolderManager).GetField("_installID", BindingFlags.Static | BindingFlags.NonPublic)!;

        /// <summary>
        /// The environment variable that controls portable mode
        /// </summary>
        private static readonly string PortableModeEnvName = $"{AutoUpdateSettings.AppName}__{DataFolderManager.PORTABLE_MODE_OPTION.Replace('-', '_')}".ToUpperInvariant();

        [SetUp]
        public void SetUp()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "dupid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);

            m_savedHomeEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
            m_savedPortableEnv = Environment.GetEnvironmentVariable(PortableModeEnvName);
            m_savedAllowInsecureEnv = Environment.GetEnvironmentVariable(Library.Common.IO.Util.AllowInsecureDatafolderEnvVar);

            // Redirect the data folder to the empty temp folder
            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, m_tempDir);
            Environment.SetEnvironmentVariable(PortableModeEnvName, "false");
            Environment.SetEnvironmentVariable(Library.Common.IO.Util.AllowInsecureDatafolderEnvVar, "true");

            ResetCachedIds();
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, m_savedHomeEnv);
            Environment.SetEnvironmentVariable(PortableModeEnvName, m_savedPortableEnv);
            Environment.SetEnvironmentVariable(Library.Common.IO.Util.AllowInsecureDatafolderEnvVar, m_savedAllowInsecureEnv);

            // Do not leak the temp-folder ids into other tests in the same process
            ResetCachedIds();
            DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);

            try
            {
                if (Directory.Exists(m_tempDir))
                    Directory.Delete(m_tempDir, true);
            }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Resets the cached id values in <see cref="DataFolderManager"/>
        /// </summary>
        private static void ResetCachedIds()
        {
            m_machineIdField.SetValue(null, null);
            m_installIdField.SetValue(null, null);
        }

        /// <summary>
        /// Verifies that reading the ids before the data folder is initialized does not
        /// cache the empty value, and that a read after initialization returns the real ids.
        /// </summary>
        [Test]
        public void IdsAreNotCachedWhenFilesAreMissing()
        {
            // Reading the ids before the data folder is initialized must not cache the
            // empty value; in debug builds the read throws to reveal early callers
#if DEBUG
            Assert.Throws<InvalidOperationException>(() => { var _ = DataFolderManager.GetMachineID(); });
            Assert.Throws<InvalidOperationException>(() => { var _ = DataFolderManager.GetInstallID(); });
#else
            Assert.AreEqual("", DataFolderManager.GetMachineID());
            Assert.AreEqual("", DataFolderManager.GetInstallID());
#endif

            // Initialize the data folder, which creates the id files
            DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ReadWritePermissionSet);

            // A later read picks up the real values, proving the empty values were not cached
            var machineId = DataFolderManager.GetMachineID();
            var installId = DataFolderManager.GetInstallID();

            Assert.IsFalse(string.IsNullOrWhiteSpace(machineId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(installId));

            // On a fresh data folder the machine id defaults to the install id
            Assert.AreEqual(installId, machineId);

            // The files on disk match the reported values
            Assert.AreEqual(machineId, File.ReadAllLines(Path.Combine(m_tempDir, "machineid.txt"))[0].Trim());
            Assert.AreEqual(installId, File.ReadAllLines(Path.Combine(m_tempDir, "installation.txt"))[0].Trim());
        }

        /// <summary>
        /// Verifies that an existing but empty id file is treated as a stable state:
        /// the empty value is cached and the file is not re-read on every access.
        /// </summary>
        [Test]
        public void ExistingEmptyIdFileIsCached()
        {
            // Create empty id files
            var machineIdPath = Path.Combine(m_tempDir, "machineid.txt");
            var installIdPath = Path.Combine(m_tempDir, "installation.txt");
            File.WriteAllText(machineIdPath, "");
            File.WriteAllText(installIdPath, "");

            // The file exists, so the empty value is a stable state and does not throw
            Assert.AreEqual("", DataFolderManager.GetMachineID());
            Assert.AreEqual("", DataFolderManager.GetInstallID());

            // Write real ids; the cached empty values must still be returned,
            // proving the file is not re-read on every access
            File.WriteAllText(machineIdPath, "machine-id-written-later");
            File.WriteAllText(installIdPath, "install-id-written-later");

            Assert.AreEqual("", DataFolderManager.GetMachineID());
            Assert.AreEqual("", DataFolderManager.GetInstallID());
        }
    }
}
