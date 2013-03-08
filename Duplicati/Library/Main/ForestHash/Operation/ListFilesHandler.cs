using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class ListFilesHandler : IDisposable
    {
        private string m_backendurl;
        private FhOptions m_options;
        private RestoreStatistics m_stat;

        public ListFilesHandler(string backend, FhOptions options, RestoreStatistics stat)
        {
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
        }

        public List<string> Run()
        {
#if DEBUG
            if (!m_options.NoLocalDb)
#endif
            //Use a speedy local query
            if (System.IO.File.Exists(m_options.Fhdbpath))
                using (var db = new Database.Localdatabase(m_options.Fhdbpath, "ListFiles"))
                {
                    var filesetid = db.GetFilesetID(m_options.RestoreTime);
                    return 
                        (from n in db.GetFiles(filesetid) 
                         select n.Path).ToList();
                }

            // Otherwise, grab info from remote location
            using (var tmpdb = new Utility.TempFile())
            using (var db = new Database.Localdatabase(tmpdb, "ListFiles"))
            using (var backend = new FhBackend(m_backendurl, m_options, db, m_stat))
            {
                var filter = RestoreHandler.FilterFilelist(m_options.RestoreTime);
                var fileset = filter(from n in backend.List()
                            let p = Volumes.VolumeBase.ParseFilename(n)
                            where p != null
                            select p).FirstOrDefault();

                if (fileset == null)
                    throw new Exception("No filesets found on remote target");

                var filename = fileset.File.Name;
                using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(filename), backend.Get(filename, -1, null), m_options))
                    return (from n in rd.Files
                            select n.Path).ToList();
            }

        }

        public void Dispose()
        {
        }
    }
}
