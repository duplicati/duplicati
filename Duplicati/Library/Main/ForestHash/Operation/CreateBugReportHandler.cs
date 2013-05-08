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
using Duplicati.Library.Main.ForestHash.Database;

namespace Duplicati.Library.Main.ForestHash.Operation
{
	public class CreateBugReportHandler : IDisposable
	{
        private string m_targetpath;
        private Options m_options;
        private CommunicationStatistics m_stat;

		public CreateBugReportHandler(string targetpath, Options options, CommunicationStatistics stat)
		{
            m_targetpath = targetpath;
            m_options = options;
            m_stat = stat;
		}
		
		public void Run()
		{
			if (System.IO.File.Exists(m_targetpath))
				throw new Exception(string.Format("Output file already exists, not overwriting {0}"));
				
			if (!System.IO.File.Exists(m_options.Dbpath))
				throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));
				
			m_stat.LogMessage("Scrubbing filenames from database, this may take a while, please wait");

			System.IO.File.Copy(m_options.Dbpath, m_targetpath);
			using(var db = new LocalBugReportDatabase(m_targetpath))
				db.Fix();
				
			m_stat.LogMessage("Completed! Please examine the log table of the database to see that no filenames are accidentially left over. If you are conserned about privay, do not attach the database to an issue!!!");
		}

		#region IDisposable implementation

		public void Dispose()
		{
		}

		#endregion
	}
}

