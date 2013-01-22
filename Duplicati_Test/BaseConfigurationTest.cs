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
using Duplicati.Library.Utility;
using Duplicati.Library.Main;
using System.Data.LightDatamodel;

namespace Duplicati_Test
{
    public abstract class BaseConfigurationTest : BaseDuplicatiTest
    {
        protected TempFolder m_config_dir;
        protected System.Data.IDbConnection m_db_connection;
        protected IDataFetcherWithRelations m_datafetcher;

        [SetUp()]
        public override void SetUp()
        {
            this.m_config_dir = new TempFolder();
            initializeDbConnection();
        }
        
        protected void reinitializeDbConnection() {
            disposeDbConnection();
            initializeDbConnection();
        }
        
        protected void initializeDbConnection() {
            this.m_db_connection = Utils.CreateDbConnection();
            this.m_datafetcher = Utils.InitializeDbConnection(this.m_config_dir, m_db_connection);
        }
        
        protected void disposeDbConnection() {
            this.m_datafetcher.Dispose();
            this.m_db_connection.Dispose();
        }
        
        [TearDown()]
        public override void TearDown()
        {
            disposeDbConnection();
            // destroy the config dir
            this.m_config_dir.Dispose();
        }
    }
}