using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.UnitTest
{

    public class ToolTests : BasicSetupHelper
    {

        [Test]
        [Category("Tools")]
        public void TestRemoteSynchronization()
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            var base_path = $"{home}/git/duplicati-carl/rsync";
            var l1 = $"{base_path}/l1";
            var l2 = $"{base_path}/l2";
            var l1r = $"{base_path}/l1_restore";
            var l2r = $"{base_path}/l2_restore";
            var backup_path = $"{home}/tmp/adaptivecpp";

            Dictionary<string, string> options = new ()
            {
                ["passphrase"] = "1234"
            };

            // Create the directories if they do not exist
            foreach (var p in new string[] { base_path, l1, l2, l1r, l2r })
            {
                if (!SystemIO.IO_OS.DirectoryExists(p))
                    SystemIO.IO_OS.DirectoryCreate(p);
            }

            // Backup the first level
            using (var c = new Controller($"file://{l1}", options, null))
            {
                var results = c.Backup([backup_path]);
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1}");
            }

            // TODO Call the remote synchronization tool
            var exe = RemoteSynchronization.Program.Main;

            options["restore-path"] = l1r;
            using (var c = new Controller($"file://{l1}", options, null))
            {
                var results = c.Restore([Path.Combine(backup_path, "*")]);
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]}");
            }

            // Try to restore the second level
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                var results = c.Restore([]);
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]}");
            }
        }
    }

}