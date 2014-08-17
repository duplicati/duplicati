//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.CommandLine.MirrorTool
{
    public class DbListSource : IEnumerable<DbTypes.File>, IDisposable
    {
        private SimpleORM m_con;
        public DbListSource(System.Data.IDbConnection con)
        {
            m_con = new SimpleORM(con);
        }
    
        public void Update(string path, long local, long remote)
        {
            m_con.InsertOrReplace(new DbTypes.File[] { new DbTypes.File() { Path = path, Local = local, Remote = remote } });
        }

        #region IEnumerable implementation
        public IEnumerator<Duplicati.CommandLine.MirrorTool.DbTypes.File> GetEnumerator()
        {
            return m_con.ReadFromDb<DbTypes.File>("1=1 ORDER BY Path").GetEnumerator();
        }
        #endregion
        #region IEnumerable implementation
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion
    }
}

