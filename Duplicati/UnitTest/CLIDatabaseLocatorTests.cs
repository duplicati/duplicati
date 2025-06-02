using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Duplicati.Library.Main;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class CLIDatabaseLocatorTests
    {
        [Test]
        [Category("CLIDatabaseLocator")]
        public void GenerateRandomNameProducesValidName()
        {
            var name = CLIDatabaseLocator.GenerateRandomName();
            Assert.AreEqual(10, name.Length, "Length should be 10 characters");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Z]{10}$"), "Name should consist of uppercase letters");

            var path = Path.Combine(Path.GetTempPath(), name + ".sqlite");
            Assert.IsTrue(CLIDatabaseLocator.IsRandomlyGeneratedName(path));
            Assert.IsFalse(CLIDatabaseLocator.IsRandomlyGeneratedName(Path.Combine(Path.GetTempPath(), "notrandom.sqlite")));
        }

        [Test]
        [Category("CLIDatabaseLocator")]
        public void GetAllDatabasePathsAndInUseWorks()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var oldEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, tempDir);
            try
            {
                // Ensure override flag is set by calling GetDataFolder
                DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ReadWritePermissionSet);

                var db1 = Path.Combine(tempDir, "a.sqlite");
                var db2 = Path.Combine(tempDir, "b.sqlite");
                var configs = new List<CLIDatabaseLocator.BackendEntry>
                {
                    new CLIDatabaseLocator.BackendEntry { Type="file", Server="localhost", Path="/", Prefix="dup", Username="u", Port=0, Databasepath=db1, ParameterFile=null },
                    new CLIDatabaseLocator.BackendEntry { Type="file", Server="localhost", Path="/", Prefix="dup", Username="u", Port=0, Databasepath=db2, ParameterFile=null }
                };
                var file = Path.Combine(tempDir, "dbconfig.json");
                File.WriteAllText(file, JsonConvert.SerializeObject(configs));

                var paths = CLIDatabaseLocator.GetAllDatabasePaths();
                Assert.That(paths, Is.EquivalentTo(new[]{db1, db2}));

                Assert.IsTrue(CLIDatabaseLocator.IsDatabasePathInUse(db1));
                Assert.IsFalse(CLIDatabaseLocator.IsDatabasePathInUse(Path.Combine(tempDir, "other.sqlite")));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, oldEnv);
                // reset override by calling without env var
                DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
