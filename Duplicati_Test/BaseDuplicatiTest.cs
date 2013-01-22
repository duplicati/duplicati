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
using System.Linq;
using Duplicati.Library.Utility;
using Duplicati.Datamodel;
using Duplicati.Server;
using Duplicati.Library.Backend;
using System.Collections.Generic;

namespace Duplicati_Test
{
    // Base class for Duplicati NUnit tests
    public abstract class BaseDuplicatiTest : InheritableTestCase
    {
        // helper that initializes the Program.DataConnection and invokes a callback
        protected static void WithProgramDbConnection(TempFolder tf, Action<IDataFetcherWithRelations> action) {
            using(System.Data.IDbConnection con = Utils.CreateDbConnection())
            {
                Utils.InitializeDbConnection(tf, con);
                action(Duplicati.GUI.Program.DataConnection);
            }
        }
        
        // helper that invokes a closure with a loaded test Duplicati applications settings database
        protected static void WithApplicationSettingsDb(TempFolder tf, Action<TempFolder, ApplicationSettings> action)
        {
            WithProgramDbConnection(tf, (dataFetcher) => {
                var appSettings = new ApplicationSettings(dataFetcher);
                
                action(tf, appSettings);
                
                dataFetcher.CommitRecursive(dataFetcher.GetObjects<ApplicationSetting>());

            });
        }
    }
}