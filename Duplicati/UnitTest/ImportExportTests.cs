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
using System.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serializable;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Backup = Duplicati.Server.Database.Backup;

namespace Duplicati.UnitTest
{
    public class ImportExportTests : BasicSetupHelper
    {
        private string serverDatafolder;

        [OneTimeSetUp]
        public void Setup()
        {
            this.serverDatafolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(this.serverDatafolder);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(this.serverDatafolder, true);
            }
            catch
            {
                // If there's an exception, let the OS deal with cleaning up the temp folder.
            }
        }

        private IBackup CreateBackup(string name, string username, string password, Dictionary<string, string> metadata)
        {
            return new Backup
            {
                Description = "Mock Backup Description",
                Filters = new IFilter[0],
                ID = "1",
                Metadata = metadata,
                Name = name,
                Settings = new[] { new Setting { Name = "passphrase", Value = "12345" } },
                Sources = new[] { "Mock Backup Source" },
                Tags = new[] { "Tags" },
                TargetURL = $"file:///mock_backup_target?auth-username={username}&auth-password={password}"
            };
        }

        [Test]
        [Category("ImportExport")]
        [TestCase(true)]
        [TestCase(false)]
        public void ExportToJSONEncoding(bool removePasswords)
        {
            Dictionary<string, string> advancedOptions = new Dictionary<string, string> { { "server-datafolder", this.serverDatafolder } };

            string usernameKey = "auth-username";
            string passwordKey = "auth-password";
            string username = @"user%40email.com";
            string password = @"abcde12345!@#$%/\";

            IBackup backup = this.CreateBackup("backup", username, password, new Dictionary<string, string>());
            if (removePasswords)
            {
                BackupImportExportHandler.RemovePasswords(backup);
            }

            Assert.That(backup.TargetURL, Does.Contain($"{usernameKey}={username}"));
            if (removePasswords)
            {
                Assert.That(backup.TargetURL, Does.Not.Contain(passwordKey));
                Assert.That(backup.TargetURL, Does.Not.Contain(password));
            }
            else
            {
                Assert.That(backup.TargetURL, Does.Contain(passwordKey));
                Assert.That(backup.TargetURL, Does.Contain(password));
            }

            byte[] jsonByteArray;
            using (Program.DataConnection = Program.GetDatabaseConnection(advancedOptions, true))
            {
                jsonByteArray = BackupImportExportHandler.ExportToJSON(Program.DataConnection, backup, null);
            }

            // The username should not have the '%40' converted to '@' since the import code
            // cannot handle it (see issue #3619).
            string json = System.Text.Encoding.Default.GetString(jsonByteArray);
            Assert.That(json, Does.Not.Contain("user@email.com"));

            ImportExportStructure importedConfiguration = Serializer.Deserialize<ImportExportStructure>(new StreamReader(new MemoryStream(jsonByteArray)));
            Assert.AreEqual(backup.Description, importedConfiguration.Backup.Description);
            Assert.AreEqual(backup.Name, importedConfiguration.Backup.Name);
            Assert.AreEqual(backup.TargetURL, importedConfiguration.Backup.TargetURL);
        }

        [Test]
        [Category("ImportExport")]
        public void RoundTrip()
        {
            Dictionary<string, string> metadata = new Dictionary<string, string> { { "SourceFilesCount", "1" } };
            Dictionary<string, string> advancedOptions = new Dictionary<string, string> { { "server-datafolder", this.serverDatafolder } };
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<INotificationUpdateService, NotificationUpdateService>();
            serviceCollection.AddSingleton(new EventPollNotify());
            FIXMEGlobal.Provider = new DefaultServiceProviderFactory().CreateServiceProvider(serviceCollection);

            using (Program.DataConnection = Program.GetDatabaseConnection(advancedOptions, true))
            {
                // Unencrypted file, don't import metadata.
                string unencryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithoutMetadata, BackupImportExportHandler.ExportToJSON(Program.DataConnection, this.CreateBackup("unencrypted without metadata", "user", "password", metadata), null));
                BackupImportExportHandler.ImportBackup(Program.DataConnection, unencryptedWithoutMetadata, false, () => null);
                Assert.AreEqual(1, Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Program.DataConnection.Backups[0].Metadata.Count);

                // Unencrypted file, import metadata.
                string unencryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithMetadata, BackupImportExportHandler.ExportToJSON(Program.DataConnection, this.CreateBackup("unencrypted with metadata", "user", "password", metadata), null));
                BackupImportExportHandler.ImportBackup(Program.DataConnection, unencryptedWithMetadata, true, () => null);
                Assert.AreEqual(2, Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Program.DataConnection.Backups[1].Metadata.Count);

                // Encrypted file, don't import metadata.
                string encryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                string passphrase = "abcde";
                File.WriteAllBytes(encryptedWithoutMetadata, BackupImportExportHandler.ExportToJSON(Program.DataConnection, this.CreateBackup("encrypted without metadata", "user", "password", metadata), passphrase));
                BackupImportExportHandler.ImportBackup(Program.DataConnection, encryptedWithoutMetadata, false, () => passphrase);
                Assert.AreEqual(3, Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Program.DataConnection.Backups[2].Metadata.Count);

                // Encrypted file, import metadata.
                string encryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(encryptedWithMetadata, BackupImportExportHandler.ExportToJSON(Program.DataConnection, this.CreateBackup("encrypted with metadata", "user", "password", metadata), passphrase));
                BackupImportExportHandler.ImportBackup(Program.DataConnection, encryptedWithMetadata, true, () => passphrase);
                Assert.AreEqual(4, Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Program.DataConnection.Backups[3].Metadata.Count);

                // Encrypted file, incorrect passphrase.
                Assert.Throws(Is.InstanceOf<Exception>(), () => BackupImportExportHandler.ImportBackup(Program.DataConnection, encryptedWithMetadata, true, () => passphrase + " "));
            }
        }
    }
}