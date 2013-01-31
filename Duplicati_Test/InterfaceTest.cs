//  Copyright (C) 2012, Aaron Hamid
//  https://github.com/ahamid, aaron.hamid@gmail.com
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
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;


namespace Duplicati_Test
{
    // Tests Duplicati.Library.Main.Interface
    [TestFixture()]
	public class InterfaceTest : BaseBackupTest
    {
     
        public InterfaceTest() : base("file to backup") {}
                
        [Test()]
        public void TestBackupWithoutSourceFiles()
        {
            AssertHelper.Throws(() => Interface.Backup(new string[0], this.m_backend_url, this.m_options), "No source folders specified for backup");
        }

        [Test()]
        public void TestBackup ()
        {
            // e.g.:
            // duplicati-full-manifestA.20121230T201547Z.manifest.aes
            // duplicati-full-content.20121230T201547Z.vol1.zip.aes
            // duplicati-full-signature.20121230T201547Z.vol1.zip.aes
            string[] BACKUP_FILES = new[] {
                "^duplicati-full-manifest.*\\.aes$",
                "^duplicati-full-content.*\\.aes$",
                "^duplicati-full-signature.*\\.aes$"
            };
            
            Interface.Backup(new string[] { this.m_source_dir }, this.m_backend_url, this.m_options);
            
            string[] backupFiles = System.IO.Directory.GetFiles(this.m_dest_dir);

            foreach (string expected_file in BACKUP_FILES) {
                Assert.AreEqual(1, backupFiles.Where(path => Regex.Match(System.IO.Path.GetFileName(path), expected_file).Success).Count(), "Expected backup file not found: " + expected_file);
            }
                            
            using (var restore_target = new TempFolder()) {
                Interface.Restore(this.m_backend_url, new string[] { restore_target }, this.m_options);
                
                // backed-up files should be restored
                AssertHelper.AssertFilesMatch(restore_target, this.m_source_files.Select(x => (string) x).ToArray());
            };
        }
    }
}
