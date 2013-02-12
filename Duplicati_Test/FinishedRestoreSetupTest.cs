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
using System.Data.LightDatamodel;
using System.ComponentModel;
using System.Windows.Forms.Wizard;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Duplicati.GUI;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using Duplicati.Datamodel;
using Duplicati.Server;
using Duplicati.GUI.Wizard_pages.RestoreSetup;

namespace Duplicati_Test
{
    // Tests Duplicati.GUI.Wizard_pages.RestoreSetup.FinishedRestoreSetup
    [TestFixture()]
	public class FinishedRestoreSetupTest : BaseBackupTest
    {
     
        public FinishedRestoreSetupTest() : base("FinishedRestoreSetupTest backup content") {}
        
        protected void performBackup()
        {
            // Just disable encryption since there is no easy way to pass the passphrase
            // to the FinishedRestoreSetup dialog
            //Duplicati.GUI.Program.UseDatabaseEncryption = false;
            //appSettings.RawOptions["no-encryption"] = "true";
            //appSettings.RawOptions["passphrase"] = "";
            this.m_options["no-encryption"] = "true";
            
            Interface.Backup(new string[] { this.m_source_dir }, this.m_backend_url, this.m_options);
        }
        
        // performs configuration prerequisites and invokes FinishedRestoreSetup.Restore
        protected void performRestore(Action<string> action)
        {
            using (TempFolder restore_dir = new TempFolder())
            {
                // Set up program state needed to be present during UI-driven restore
                // we don't really care, we just need these fields to be present
                Duplicati.GUI.Program.LiveControl = new LiveControls(new ApplicationSettings(this.m_datafetcher));
                // hopefully scheduler doesn't schedule anything; initialize WorkerThread as paused
                Duplicati.GUI.Program.Scheduler = new Scheduler(this.m_datafetcher, new WorkerThread<IDuplicityTask>(null, true), Duplicati.GUI.Program.MainLock);
    
                var page = new FinishedRestoreSetup();
                var dialog = new Dialog(new[] { page });
                // use the secret handshake to set the Backend configuration properties
                dialog.Settings["WSW_Backend"] = "file";
                dialog.Settings["WSW_BackendSettings"] = new Dictionary<string, string>() {
                    { "Destination", this.m_dest_dir },
                    //{ "passphrase", this.m_passphrase },
                    { "no-encryption", "true" }
                };
                
                // causes the FinishedRestoreSetup page to be entered
                dialog.CurrentPage = page;
                // invoke page.Restore(null, null) via reflection
                typeof(FinishedRestoreSetup).InvokeMember("Restore", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, page, new object[] { null, null });
                
                action(restore_dir);
            }
        }
  
        // tests that control file (database) is restored to the content that was backed up
        [Test()]
        public void TestRestore() {
            const bool BACKUP_VALUE = true;
            const bool MODIFIED_VALUE = !BACKUP_VALUE;
            
            var appSettings = new ApplicationSettings(this.m_datafetcher);
            // set a parameter in order to verify restore succeeded
            appSettings.HideDonateButton = BACKUP_VALUE;
            this.m_datafetcher.CommitRecursive(this.m_datafetcher.GetObjects<ApplicationSetting>());
            
            performBackup();
            
            // modify current value
            appSettings.HideDonateButton = MODIFIED_VALUE;
            this.m_datafetcher.CommitRecursive(this.m_datafetcher.GetObjects<ApplicationSetting>());
            
            // reload the db to sanity check that the changed value is on disk
            this.reinitializeDbConnection();
            Assert.AreEqual(MODIFIED_VALUE, new ApplicationSettings(this.m_datafetcher).HideDonateButton);

            // restore control files
            performRestore((restore_dir) => {
                // settings database should be restored
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(m_config_dir, Utils.TEST_DB)));
    
                // reload the db so we can confirm that the backed-up version was restored
                this.reinitializeDbConnection();
                
                // settings db should be restored to initial backed-up state
                appSettings = new ApplicationSettings(this.m_datafetcher);
                Assert.AreEqual(BACKUP_VALUE, appSettings.HideDonateButton, "Settings database was not restored!");
            });
        }
        
        // tests that invalid settings are normalized/defaulted again after a restore of the settings database
        // to handle the situation that the db is restored in a different context/different computer
        [Test()]
        public void TestSettingsNormalization() {
            var appSettings = new ApplicationSettings(this.m_datafetcher);
            
            // save some invalid settings
            appSettings.StartupDelayDuration = "";
            appSettings.TempPath = "this path does should not exist!";
            appSettings.SignatureCachePath = "this path also should not exist!";
            this.m_datafetcher.CommitRecursive(this.m_datafetcher.GetObjects<ApplicationSetting>());
            
            performBackup();
   
            // make sure paths were not created as side-effect of backup
            Assert.IsFalse(System.IO.Directory.Exists("this path should not exist!"));
            Assert.IsFalse(System.IO.Directory.Exists("this path also should not exist!"));

            // set to alternate values to ensure
            // 1) TempPath is not automatically created during restore
            // 2) non-empty value ensures we can detect that normalization actually reset values
            appSettings.TempPath = "temp path that should be overwitten";
            appSettings.SignatureCachePath = "signature cache path that should be overwitten";
            this.m_datafetcher.CommitRecursive(this.m_datafetcher.GetObjects<ApplicationSetting>());

            // restore control files
            performRestore((restore_dir) => {
                // reload the db so we can confirm that the settings were normalized
                this.reinitializeDbConnection();
                appSettings = new ApplicationSettings(this.m_datafetcher);
                Assert.AreEqual("5m", appSettings.StartupDelayDuration);
                Assert.AreEqual(ApplicationSettings.DefaultTempPath, appSettings.TempPath);
                Assert.AreEqual(ApplicationSettings.DefaultSignatureCachePath, appSettings.SignatureCachePath);
            });
        }
    }
}