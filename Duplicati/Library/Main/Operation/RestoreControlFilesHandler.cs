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
        private string m_target;
        private RestoreControlFilesResults m_result;

        public RestoreControlFilesHandler(string backendurl, Options options, string target, RestoreControlFilesResults result)
        {
            m_options = options;
            m_target = target;
            m_backendurl = backendurl;
            m_result = result;
        }

        public void Run()
        {
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(System.IO.File.Exists(m_options.Dbpath) ? m_options.Dbpath : (string)tmpdb, "RestoreControlFiles"))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
            	try
            	{
	                var files = from file in backend.List()
	                            let p = Volumes.VolumeBase.ParseFilename(file)
	                            where p != null && p.FileType == RemoteVolumeType.Files
	                            orderby p.Time
	                            select file;
	
	                Exception lastEx = new Exception("No suitable files found on remote target");
	
	                foreach(var file in files)
	                    try
	                    {
	                        long size;
	                        string hash;
	                        RemoteVolumeType type;
	                        RemoteVolumeState state;
	                        if (!db.GetRemoteVolume(file.Name, out hash, out size, out type, out state))
	                            size = file.Size;
	
							using (var tmpfile = backend.Get(file.Name, size, hash))
	                        using (var tmp = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(file.Name), tmpfile, m_options))
	                            foreach (var cf in tmp.ControlFiles)
	                                using (var ts = System.IO.File.Create(System.IO.Path.Combine(m_target, cf.Key)))
	                                    Library.Utility.Utility.CopyStream(cf.Value, ts);
	                        
	                        lastEx = null;
	                        break;
	                    }
	                    catch(Exception ex)
	                    {
	                        lastEx = ex;
	                    }
	
	                if (lastEx != null)
	                    throw lastEx;
	        	}
	        	finally
	        	{
	        		backend.WaitForComplete(db, null);
	        	}
            }
        }
    }
}
