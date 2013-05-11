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
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation
{
	internal class DeleteHandler : CompactHandler
	{
		private class DeleteResults : IDeleteResults
		{
	        public IEnumerable<DateTime> DeletedSets { get; private set; }
        	public bool Dryrun { get; private set; }
			
			public DeleteResults(IEnumerable<DateTime> deletedSets, bool dryrun)
			{
				DeletedSets = deletedSets;
				Dryrun = dryrun;
			}
		}
	
		public DeleteHandler(string backend, Options options, CommunicationStatistics stat)
	        : base(backend, options, stat)
        {
        }

        public new IDeleteResults Run()
		{		
			if (!System.IO.File.Exists(m_options.Dbpath))
				throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

			using (var db = new Database.LocalDeleteDatabase(m_options.Dbpath, false))
			using(var tr = db.BeginTransaction())
			{
	        	Utility.VerifyParameters(db, m_options);
	        	
				using (var backend = new BackendManager(m_backendurl, m_options, m_stat, db))
				{
					if (!m_options.NoBackendverification)
						FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_stat); 
					
                    var toDelete = m_options.GetFilesetsToDelete(db.FilesetTimes.Select(x => x.Value).ToArray());
                                        
					var count = 0L;
					foreach(var f in db.DropFilesetsFromTable(toDelete, tr))
					{
						count++;
						if (m_options.Force && !m_options.Dryrun)
							backend.Delete(f);
						else
							m_stat.LogMessage("[Dryrun] - Would delete remote fileset: {0}", f);
					}
					
					backend.WaitForComplete(db, tr);
					
					if (m_options.Force && !m_options.Dryrun)
					{
						if (count == 0)
							m_stat.LogMessage("No remote filesets were deleted");
						else
							m_stat.LogMessage("Deleted {0} remote fileset(s)", count);
					}
					else
					{
					
						if (count == 0)
							m_stat.LogMessage("No remote filesets would be deleted");
						else
							m_stat.LogMessage(string.Format("{0} remote fileset(s) would be deleted", count));

						if (count > 0 && !m_options.Force)
							m_stat.LogMessage("Specify --force to actually delete files");
					}
					
					if (!m_options.NoAutoCompact)
						m_stat.LogMessage(base.DoCompact(db, true, tr));
					
					if (m_options.Force && !m_options.Dryrun)
						tr.Commit();
					else
						tr.Rollback();
					
					return new DeleteResults(toDelete, !(m_options.Force && !m_options.Dryrun));
				}
			}
			
        }
	}
}

