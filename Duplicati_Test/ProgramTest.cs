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
using NUnit.Framework;
using Duplicati.GUI;
using Duplicati.Library.Utility;
using Duplicati.Datamodel;
using Duplicati.Server;

namespace Duplicati_Test
{
    // Tests Duplication.GUI.Program
    [TestFixture()]
	public class ProgramTest : BaseConfigurationTest
    {
        // Test initial Program UseDbEncryption flag state
        [Test()]
        public void TestInitialUseDbEncryptionValue() {
            Assert.IsFalse(Duplicati.GUI.Program.UseDatabaseEncryption);
        }
        
        // Tests that the a new application settings db is created if missing, and yields default path values        [Test()]
        public void TestApplicationSettingsInitialization()
        {
            WithApplicationSettingsDb(this.m_config_dir, (tf, appSettings) => {
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(tf, "Duplicati_Test.sqlite")));
                Assert.AreEqual(ApplicationSettings.DefaultTempPath, appSettings.TempPath);
                Assert.AreEqual(ApplicationSettings.DefaultSignatureCachePath, appSettings.SignatureCachePath);
            });
        }
        
        // Tests that if we explicitly overwrite TempPath and SignatureCachePath to empty values, then default values will be used
        [Test()]
        public void TestResetPaths()
        {
            
            // set non-default values
            WithApplicationSettingsDb(this.m_config_dir, (tf, appSettings) => {
                Assert.AreEqual(ApplicationSettings.DefaultTempPath, appSettings.TempPath);
                Assert.AreEqual(ApplicationSettings.DefaultSignatureCachePath, appSettings.SignatureCachePath);
                
                appSettings.TempPath= "NewTempPath";
                appSettings.SignatureCachePath = "NewSignatureCachePath";
            });
            
            // unset values
            WithApplicationSettingsDb(this.m_config_dir, (tf, appSettings) => {
                Assert.AreEqual("NewTempPath", appSettings.TempPath);
                Assert.AreEqual("NewSignatureCachePath", appSettings.SignatureCachePath);
                
                appSettings.TempPath = "";
                appSettings.SignatureCachePath = "";
            });
            
            // check that they are reset to defaults
            WithApplicationSettingsDb(this.m_config_dir, (tf, appSettings) => {
                Assert.AreEqual(ApplicationSettings.DefaultTempPath, appSettings.TempPath);
                Assert.AreEqual(ApplicationSettings.DefaultSignatureCachePath, appSettings.SignatureCachePath);
            });
        }
    }
}