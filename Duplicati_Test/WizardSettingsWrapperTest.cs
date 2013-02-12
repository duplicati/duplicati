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
using NUnit.Framework;
using Duplicati.GUI.Wizard_pages;

namespace Duplicati_Test
{
    [TestFixture()]
	public class WizardSettingsWrapperTest
    {
        // Tests setting and getting a Wizard setting
        [Test()]
        public void TestWizardSetting()
        {
            var dict = new Dictionary<string, object>();
            dict["WSW_Backend"] = "file";
            var wsw = new WizardSettingsWrapper(dict);
            Assert.AreEqual("file", wsw.Backend);
        }
    }
}