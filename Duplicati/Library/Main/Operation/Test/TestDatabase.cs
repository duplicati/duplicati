//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using static Duplicati.Library.Main.Database.LocalTestDatabase;

namespace Duplicati.Library.Main.Operation.Test
{
    internal class TestDatabase : Common.DatabaseCommon
    {
        private readonly LocalTestDatabase m_database;

        public TestDatabase(LocalTestDatabase db, Options options)
            : base(db, options)
        {
            m_database = db;
        }

        public Task<IFilelist> CreateFilelistAsync(string name)
        {
            return RunOnMain(() => m_database.CreateFilelist(name, m_transaction));
        }

        public Task<IBlocklist> CreateBlocklistAsync(string name)
        {
            return RunOnMain(() => m_database.CreateBlocklist(name, m_transaction));
        }

        public Task<IIndexlist> CreateIndexlistAsync(string name)
        {
            return RunOnMain(() => m_database.CreateIndexlist(name, m_transaction));
        }

    }
}
