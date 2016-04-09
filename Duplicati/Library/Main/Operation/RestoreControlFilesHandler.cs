using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreControlFilesHandler
    {
        private Options m_options;
        private string m_backendurl;
        private RestoreControlFilesResults m_result;

        public RestoreControlFilesHandler(string backendurl, Options options, RestoreControlFilesResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;
        }

        public void Run(IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null)
        {
            if (string.IsNullOrEmpty(m_options.Restorepath))
                throw new Exception("Cannot restore control files without --restore-path");
            if (!System.IO.Directory.Exists(m_options.Restorepath))
                System.IO.Directory.CreateDirectory(m_options.Restorepath);
        
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(System.IO.File.Exists(m_options.Dbpath) ? m_options.Dbpath : (string)tmpdb, "RestoreControlFiles", true))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
                
                var filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(filterstrings), compositefilter);
                
            	try
            	{
                    var filteredList = ListFilesHandler.ParseAndFilterFilesets(backend.List(), m_options);
                    if (filteredList.Count == 0)
                        throw new Exception("No filesets found on remote target");
    
	                Exception lastEx = new Exception("No suitable files found on remote target");
	
	                foreach(var fileversion in filteredList)
	                    try
	                    {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(db, null);
                                return;
                            }    
                        
                            var file = fileversion.Value.File;
	                        long size;
	                        string hash;
	                        RemoteVolumeType type;
	                        RemoteVolumeState state;
	                        if (!db.GetRemoteVolume(file.Name, out hash, out size, out type, out state))
	                            size = file.Size;
	
                            var res = new List<string>();
							using (var tmpfile = backend.Get(file.Name, size, hash))
	                        using (var tmp = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(file.Name), tmpfile, m_options))
	                            foreach (var cf in tmp.ControlFiles)
                                    if (Library.Utility.FilterExpression.Matches(filter, cf.Key))
                                    {
                                        var targetpath = System.IO.Path.Combine(m_options.Restorepath, cf.Key);
    	                                using (var ts = System.IO.File.Create(targetpath))
    	                                    Library.Utility.Utility.CopyStream(cf.Value, ts);
                                        res.Add(targetpath);
                                    }
	                        
                            m_result.SetResult(res);
                            
	                        lastEx = null;
	                        break;
	                    }
	                    catch(Exception ex)
	                    {
	                        lastEx = ex;
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
	                    }
	
	                if (lastEx != null)
	                    throw lastEx;
	        	}
	        	finally
	        	{
	        		backend.WaitForComplete(db, null);
	        	}

                db.WriteResults();
            }
        }
    }
}
