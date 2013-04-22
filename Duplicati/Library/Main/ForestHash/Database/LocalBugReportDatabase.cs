//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, opensource@duplicati.com
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

namespace Duplicati.Library.Main.ForestHash.Database
{
	public class LocalBugReportDatabase : LocalDatabase
	{
		public LocalBugReportDatabase(string path)
			: base(path, "BugReportCreate")
		{
		}

		public void Fix()
		{
			using(var tr = m_connection.BeginTransaction())
			using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = tr;
				
				using(var upcmd = m_connection.CreateCommand())
				{
					upcmd.Transaction = tr;
					long id = 1;
					using(var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Path"" FROM ""File"" "))
						while(rd.Read())
						{
							upcmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = replace(""Message"", ?, ?), ""Exception"" = replace(""Exception"", ?, ?)", rd.GetValue(0), id.ToString(), rd.GetValue(0), id.ToString() );
							id++;
						}
				}

				cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = ""ERASED!"" WHERE ""Message"" LIKE ""%/%"" OR ""Message"" LIKE ""%:\%"" ");				
				cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Exception"" = ""ERASED!"" WHERE ""Exception"" LIKE ""%/%"" OR ""Exception"" LIKE ""%:\%"" ");				
				cmd.ExecuteNonQuery(@"UPDATE ""File"" SET ""Path"" = ""ID"" ");
				
				tr.Commit();
				
				cmd.Transaction = null;
				cmd.ExecuteNonQuery("VACUUM");
			}
		}
	}
}

