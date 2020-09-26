//  Copyright (C) 2019, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

using System;
using System.Collections.Generic;
using System.IO;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serializable;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.Server.WebServer.RESTMethods;
using NUnit.Framework;
using Backup = Duplicati.Server.Database.Backup;

namespace Duplicati.UnitTest
{
    public class ImportExportTests
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
                Settings = new[] {new Setting {Name = "passphrase", Value = "12345"}},
                Sources = new[] {"Mock Backup Source"},
                Tags = new[] {"Tags"},
                TargetURL = $"file:///mock_backup_target?auth-username={username}&auth-password={password}"
            };
        }

        [Test]
        [Category("ImportExport")]
        [TestCase(true)]
        [TestCase(false)]
        public void ExportToJSONEncoding(bool removePasswords)
        {
            Dictionary<string, string> advancedOptions = new Dictionary<string, string> {{"server-datafolder", this.serverDatafolder}};

            string usernameKey = "auth-username";
            string passwordKey = "auth-password";
            string username = @"user%40email.com";
            string password = @"abcde12345!@#$%/\";

            IBackup backup = this.CreateBackup("backup", username, password,  new Dictionary<string, string>());
            if (removePasswords)
            {
                Server.WebServer.RESTMethods.Backup.RemovePasswords(backup);
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
            using (Program.DataConnection = Program.GetDatabaseConnection(advancedOptions))
            {
                jsonByteArray = Server.WebServer.RESTMethods.Backup.ExportToJSON(backup, null);
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
            Dictionary<string, string> metadata = new Dictionary<string, string> {{"SourceFilesCount", "1"}};
            Dictionary<string, string> advancedOptions = new Dictionary<string, string> {{"server-datafolder", this.serverDatafolder}};
            using (Program.DataConnection = Program.GetDatabaseConnection(advancedOptions))
            {
                // Unencrypted file, don't import metadata.
                string unencryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithoutMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(this.CreateBackup("unencrypted without metadata", "user", "password", metadata), null));
                Backups.ImportBackup(unencryptedWithoutMetadata, false, () => null, advancedOptions);
                Assert.AreEqual(1, Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Program.DataConnection.Backups[0].Metadata.Count);

                // Unencrypted file, import metadata.
                string unencryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(this.CreateBackup("unencrypted with metadata", "user", "password", metadata), null));
                Backups.ImportBackup(unencryptedWithMetadata, true, () => null, advancedOptions);
                Assert.AreEqual(2, Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Program.DataConnection.Backups[1].Metadata.Count);

                // Encrypted file, don't import metadata.
                string encryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                string passphrase = "abcde";
                File.WriteAllBytes(encryptedWithoutMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(this.CreateBackup("encrypted without metadata", "user", "password", metadata), passphrase));
                Backups.ImportBackup(encryptedWithoutMetadata, false, () => passphrase, advancedOptions);
                Assert.AreEqual(3, Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Program.DataConnection.Backups[2].Metadata.Count);

                // Encrypted file, import metadata.
                string encryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(encryptedWithMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(this.CreateBackup("encrypted with metadata", "user", "password", metadata), passphrase));
                Backups.ImportBackup(encryptedWithMetadata, true, () => passphrase, advancedOptions);
                Assert.AreEqual(4, Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Program.DataConnection.Backups[3].Metadata.Count);

                // Encrypted file, incorrect passphrase.
                Assert.Throws(Is.InstanceOf<Exception>(), () => Backups.ImportBackup(encryptedWithMetadata, true, () => passphrase + " ", advancedOptions));
            }
        }
    }
}