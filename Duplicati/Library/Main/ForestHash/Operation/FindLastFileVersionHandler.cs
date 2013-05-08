using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class FindLastFileVersionHandler : IDisposable
    {
        private string m_backendurl;
        private Options m_options;
        private CommunicationStatistics m_stat;

        public FindLastFileVersionHandler(string backend, Options options, CommunicationStatistics stat)
        {
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
        }

        public List<KeyValuePair<string, DateTime>> Run()
        {
        	var filelist = m_options.FileToRestore.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
			var dtnull = new DateTime(0);
#if DEBUG
            if (!m_options.NoLocalDb)
#endif
            //Use a speedy local query
            if (System.IO.File.Exists(m_options.Dbpath))
                using (var db = new LocalDatabase(m_options.Dbpath, "FindLastFileVersion"))
                {
                	var found = db.GetNewestFileset(filelist, m_options.RestoreTime).ToDictionary(x => x.Key, x => x.Value);
                	return (from n in filelist select new KeyValuePair<string, DateTime>(n, found.ContainsKey(n) ? found[n] : dtnull)).ToList();
                }
			
			m_stat.LogWarning("No local database, accessing remote store", null);

            // Otherwise, grab info from remote location
            using (var tmpdb = new Utility.TempFile())
            using (var db = new LocalDatabase(tmpdb, "FindLastFileVersion"))
            using (var backend = new FhBackend(m_backendurl, m_options, m_stat, db))
            {
                var filter = RestoreHandler.FilterFilelist(m_options.RestoreTime);
                var volumes = filter(from n in backend.List()
                            let p = VolumeBase.ParseFilename(n)
                            where p != null && p.FileType == RemoteVolumeType.Files
                            orderby p.Time
							select p).ToArray();
            
                if (volumes.Length == 0)
                    throw new Exception("No filesets found on remote target");

				var res = new Dictionary<string, DateTime>();
				foreach(var s in filelist)
					res[s] = dtnull;

            	foreach(var p in volumes)
            	{
            		using(var tmpfile = backend.Get(p.File.Name, p.File.Size, null))
            		using(var r = new FilesetVolumeReader(p.CompressionModule, tmpfile, m_options))
            			foreach(var f in r.Files)
	            		{
	            			DateTime n;
	            			if (res.TryGetValue(f.Path, out n))
	            				if (n == dtnull)
	            					res[f.Path] = p.Time;
	            		}
	            	
	            	// We are done early
	            	if (!res.Values.Contains(dtnull))
	            		break;
	            }
	            
	            backend.WaitForComplete(db, null);
            
				return res.ToList();
            }
        }

        public void Dispose()
        {
        }
    }
}
