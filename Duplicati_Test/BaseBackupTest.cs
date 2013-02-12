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
using NUnit.Framework;
using System.Data.LightDatamodel;
using Duplicati.Library.Utility;
using Duplicati.Library.Main;
using Duplicati.GUI;

namespace Duplicati_Test
{
    // Base tests which sets up and tears down configuration required for performing
    // backup and restore tests
    public abstract class BaseBackupTest : BaseConfigurationTest
    {
        protected string[] m_content;
        protected TempFolder m_source_dir;
        protected IList<TempFile> m_source_files;
        protected TempFolder m_dest_dir;
        protected string m_passphrase = "passphrase";

        protected string m_backend_url;
        
        protected Dictionary<string, string> m_options = new Dictionary<string, string>();
                
        protected BaseBackupTest(params string[] content)
        {
            this.m_content = content;
            // set passphrase to avoid console prompt
            this.m_options["passphrase"] = this.m_passphrase;
            //this.m_options["no-encryption"] = "true";
        }
        
        [SetUp()]
        public override void SetUp()
        {
            base.SetUp();
            this.m_source_dir = new TempFolder();
            this.m_source_files = Utils.GenerateFiles(this.m_source_dir, this.m_content);
            this.m_dest_dir = new TempFolder();
            this.m_backend_url = "file://" + m_dest_dir;
            // TODO: figure out how this gets properly set/defaulted
            this.m_options["signature-control-files"] = Program.DatabasePath;;
        }
        
        [TearDown()]
        public override void TearDown()
        {
            // destroy dest dir
            this.m_dest_dir.Dispose();
            // destroy source files
            foreach (var file in this.m_source_files) { file.Dispose(); }
            // destroy source dir
            this.m_source_dir.Dispose();
            base.TearDown();
        }
    }
}