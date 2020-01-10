using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RepairHandlerTests : BasicSetupHelper
    {
        public override void SetUp()
        {
            base.SetUp();
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "emptyFile"), new byte[] {0});
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public void RepairMissingIndexFiles(string noEncryption)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["no-encryption"] = noEncryption};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
            }

            string[] dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            foreach (string f in dindexFiles)
            {
                File.Delete(f);
            }

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Repair();
            }

            foreach (string file in dindexFiles)
            {
                Assert.IsTrue(File.Exists(Path.Combine(this.TARGETFOLDER, file)));
            }
        }
    }
}