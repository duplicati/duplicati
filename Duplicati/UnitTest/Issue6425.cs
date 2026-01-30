using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue6425 : BasicSetupHelper
    {
        [Test]
        [Category("Purge")]
        public void TestPurgeFileSizeCalculationWithSharedBlocks()
        {
            var blocksize = 1024 * 10; // 10KB blocks

            var testopts = TestOptions;
            testopts["blocksize"] = blocksize + "b";
            testopts["no-backend-verification"] = "true";

            var sharedContent = new byte[blocksize * 2];
            new Random(42).NextBytes(sharedContent);

            var file1 = Path.Combine(DATAFOLDER, "file1.dat");
            var file2 = Path.Combine(DATAFOLDER, "file2.dat");

            File.WriteAllBytes(file1, sharedContent);
            File.WriteAllBytes(file2, sharedContent);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var purgeResult = c.PurgeFiles(new Duplicati.Library.Utility.FilterExpression(new[] { file1 }));

                Assert.AreEqual(0, purgeResult.Errors.Count());

                Console.WriteLine($"Reported RemovedFileSize: {purgeResult.RemovedFileSize}");
                Console.WriteLine($"File size: {sharedContent.Length}");

                Assert.Less(purgeResult.RemovedFileSize, sharedContent.Length,
                    "RemovedFileSize should not count blocks still referenced by other files");
            }
        }
    }
}
