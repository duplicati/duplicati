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
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

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
            }
        }

        [Test]
        [Category("ImportExport")]
        public void RoundTrip()
        {
            Dictionary<string, string> metadata = new Dictionary<string, string> { { "SourceFilesCount", "1" } };
            IBackup CreateBackup(string name)
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
                    TargetURL = "file:///mock_backup_target"
                };
            }

            Dictionary<string, string> advancedOptions = new Dictionary<string, string> { { "server-datafolder", this.serverDatafolder } };
            using (Duplicati.Server.Program.DataConnection = Duplicati.Server.Program.GetDatabaseConnection(advancedOptions))
            {
                string unencryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithoutMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(CreateBackup("unencrypted without metadata"), null));
                Duplicati.Server.WebServer.RESTMethods.Backups.ImportBackup(unencryptedWithoutMetadata, false, () => null, advancedOptions);
                Assert.AreEqual(1, Duplicati.Server.Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Duplicati.Server.Program.DataConnection.Backups[0].Metadata.Count);

                string unencryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(unencryptedWithMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(CreateBackup("unencrypted with metadata"), null));
                Duplicati.Server.WebServer.RESTMethods.Backups.ImportBackup(unencryptedWithMetadata, true, () => null, advancedOptions);
                Assert.AreEqual(2, Duplicati.Server.Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Duplicati.Server.Program.DataConnection.Backups[1].Metadata.Count);

                string encryptedWithoutMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                string passphrase = "abcde";
                File.WriteAllBytes(encryptedWithoutMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(CreateBackup("encrypted without metadata"), passphrase));
                Duplicati.Server.WebServer.RESTMethods.Backups.ImportBackup(encryptedWithoutMetadata, false, () => passphrase, advancedOptions);
                Assert.AreEqual(3, Duplicati.Server.Program.DataConnection.Backups.Length);
                Assert.AreEqual(0, Duplicati.Server.Program.DataConnection.Backups[2].Metadata.Count);

                string encryptedWithMetadata = Path.Combine(this.serverDatafolder, Path.GetRandomFileName());
                File.WriteAllBytes(encryptedWithMetadata, Server.WebServer.RESTMethods.Backup.ExportToJSON(CreateBackup("encrypted with metadata"), passphrase));
                Duplicati.Server.WebServer.RESTMethods.Backups.ImportBackup(encryptedWithMetadata, true, () => passphrase, advancedOptions);
                Assert.AreEqual(4, Duplicati.Server.Program.DataConnection.Backups.Length);
                Assert.AreEqual(metadata.Count, Duplicati.Server.Program.DataConnection.Backups[3].Metadata.Count);
            }
        }
    }
}
